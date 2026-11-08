# Latency audit

This source-level audit covers the client's H.264/Opus pipeline, queue capacities, and possible optimization tradeoffs.

Queue capacities below are not measured latency. A queue only adds its full capacity of backlog when it actually fills. Compilation and source review do not establish input-to-display latency or hardware decoder behavior.

## Current pipeline

Video: UDP/RTP/FEC -> Moonlight decode-unit queue -> native callback -> WinRT byte array -> managed sample queue -> MediaStreamSource -> MediaElement decoder and presentation -> display.

Audio: UDP/RTP/FEC -> Moonlight packet queue -> Opus -> PCM byte array -> managed sample queue -> MediaStreamSource -> system audio output.

Input: Windows event -> page handler -> native bridge -> Moonlight input queue -> network -> host application.

## Existing low-latency settings

- `BufferTime = TimeSpan.Zero` and `CanSeek = false` in [StreamMedia.cs](../Moonlight/Core/StreamMedia.cs).
- `RealTimePlayback = True` in [PlayerPage.xaml](../Moonlight/PlayerPage.xaml).
- Event-driven physical keyboard and mouse input, without a gamepad polling timer.
- Bounded video and audio queues, with IDR recovery after video overflow.
- Native `MaxSpeed` optimization and .NET Native in Release builds.

Zero BufferTime does not remove decoder, audio-device, or presentation buffering inside Windows. Microsoft describes [MediaStreamSource.BufferTime](https://github.com/MicrosoftDocs/winrt-api/blob/docs/windows.media.core/mediastreamsource_buffertime.md) as the requested amount of sample prefetch, and [RealTimePlayback](https://github.com/MicrosoftDocs/winrt-api/blob/docs/windows.ui.xaml.controls/mediaelement_realtimeplayback.md) as a real-time playback configuration.

## Queues and ownership

| Stage | Source evidence | Capacity or cost | Possible improvement |
| --- | --- | --- | --- |
| Managed video queue | `video.Add(sample, 4, false)` | Four frames contain about 133 ms of video at 30 FPS | Consider an age budget or a smaller bound, with correct reference-frame recovery. |
| Moonlight decode-unit queue | `decodeUnitQueue` in VideoDepacketizer.c | Fifteen frames contain about 500 ms of video at 30 FPS | Avoid duplicate queues through an appropriate renderer integration. |
| Native-to-managed video | `submitVideo` in Runtime.cpp | A new array and packet-chain copy per frame, plus interop costs | Consider an owning IBuffer and a bounded pool with correct asynchronous lifetime management. |
| Managed audio queue | `audio.Add(sample, 20, true)` | Twenty typical 5-ms PCM chunks contain about 100 ms of audio | Bound by actual PCM duration; smaller budgets can cause underruns. |
| Moonlight audio queue | `packetQueue` in AudioStream.c | Thirty 5-ms packets contain about 150 ms of audio | Avoid redundant buffering when integrating a native audio renderer. |
| System media pipeline | MediaStreamSource/MediaElement | Decoder and presentation queues are not exposed by this wrapper | A native hardware MFT/D3D11 renderer could provide more control, at substantial implementation cost. |
| Sample requests | A deferral and TakeAsync for every request | Scheduling and allocation overhead, not a fixed frame delay | A synchronous ready-sample path may reduce overhead. |

Sources: [Runtime.cpp](../native/Runtime.cpp), [StreamMedia.cs](../Moonlight/Core/StreamMedia.cs), [VideoDepacketizer.c](../moonlight-common-c/src/VideoDepacketizer.c), [AudioStream.c](../moonlight-common-c/src/AudioStream.c), and [Limelight.h](../moonlight-common-c/src/Limelight.h).

These capacities must not be summed and presented as an observed delay. The managed `AsBuffer()` call wraps an existing array; it is not necessarily another full copy. The exact interop cost requires profiling.

## Optimization constraints

- **H.264 references:** dropping arbitrary compressed P-frames can break later frames. After overflow, the current implementation clears queued video, requests an IDR, and rejects P-frames until recovery. An IDR-safe replacement policy is a possible alternative, not a reason to keep only the newest compressed P-frame.
- **Direct submission:** upstream `CAPABILITY_DIRECT_SUBMIT` runs the callback on the receive thread and requires nonblocking handling. The current callback allocates, copies, crosses into managed code, and takes a queue lock. Enabling the flag alone can delay UDP reception. A pull renderer or a short bounded native callback needs a deliberate design.
- **Buffer lifetime:** pooled memory must remain owned until the media sample has finished consuming it. Reusing an array as soon as the callback returns can corrupt playback.
- **Audio/video clocks:** audio duration must come from decoded sample counts. Video PTS and audio discontinuities need a coordinated policy; moving timestamps arbitrarily can cause drift or presentation stalls.
- **Prefetch versus responsiveness:** the current source uses zero BufferTime. Additional prefetch may smooth delivery but can increase latency.
- **Hardware decoding:** Microsoft documents [MF_LOW_LATENCY](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-low-latency) on actual pipeline components and [H.264 decoder attributes](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-decoder). Adding a GUID to compressed VideoEncodingProperties does not prove the selected decoder was configured. OEM W10M support must be established separately.
- **API compatibility:** SDK 15063 does not expose MediaStreamSource.IsLive. New renderer APIs must also be available on the minimum OS build 14393.
- **Slicing and RFI:** do not declare multiple-slice or reference-frame-invalidation capabilities before confirming the decoder supports them. Reducing SPS/DPB reference buffers can damage recovery.
- **FEC and packet ordering:** removing recovery mechanisms can create more IDR stalls. The normal complete-frame path does not necessarily wait for all parity packets.
- **Thread and socket tuning:** raising every thread's priority or enlarging receive buffers is not a universal latency improvement. Packet sizes currently follow the wrapper's LAN/remote choices of 1392/1024 bytes.
- **CPU work:** official Opus ARM optimizations and crypto backend improvements are profiling candidates. Keep source builds compatible with v141 and preserve encryption.

## Input timing

Physical keyboard input uses AcceleratorKeyActivated events and the regular key-event path. UTF-8 text insertion is a separate compatibility path with upstream waits; it should not replace ordinary keyboard events for gaming.

Touch clicks are sent on release to distinguish a tap from movement and a long hold. The 50-ms delay after sending the down event is the button hold before release; it does not delay the initial down event. A separate touch-down gaming mode would require a different right-click and drag interaction.

Upstream mouse/pen batching is 1 ms and is intended to reduce queue pressure. It is not a 16-ms polling loop. See [PlayerPage.xaml.cs](../Moonlight/PlayerPage.xaml.cs) and [InputStream.c](../moonlight-common-c/src/InputStream.c).

## Stream and host settings

The client keeps 30 FPS and the 40 Mbps default. At 30 FPS, one frame period is about 33.3 ms; that is a sampling interval, not a measured minimum input-to-display delay.

Lower bitrate can reduce congestion and packet loss when bandwidth is constrained. Lower resolution can reduce decoding and scaling work, at a quality cost. Maximum bitrate is not automatically the lowest-latency setting.

Host-side candidates include a hardware encoder, GPU headroom, render-queue management, and resolution matching. Sunshine documents NVENC P1 as its fastest preset. Disabling two-pass is faster but has a documented bitrate-overshoot and packet-loss tradeoff. See [Sunshine configuration](https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2configuration.html) and [the NVIDIA encoder guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/nvenc-video-encoder-api-prog-guide/index.html).

A wired host and stable client network can help. Do not change HAGS, power policy, or MTU automatically on the assumption that they universally reduce latency. See [Moonlight troubleshooting](https://github.com/moonlight-stream/moonlight-docs/wiki/Troubleshooting).

## Validation priorities

1. Preserve VS2017/v141, SDK 15063, ARM32, and minimum OS compatibility.
2. Establish ownership, queue bounds, shutdown behavior, and H.264 recovery before introducing pooling or a new renderer.
3. Use actual PCM duration and a consistent audio/video clock.
4. Add aggregated diagnostics without a persistent streaming overlay. The core exposes RTT estimates, pending video frames, and pending audio duration; decode units include receive/enqueue timestamps and optional host processing latency.
5. Keep timestamp domains separate. Host PTS cannot be directly subtracted from a local clock.
6. Distinguish build checks and synthetic tests from device performance measurements. RTT and queue depth alone do not measure input-to-photon delay.

The upstream startup audio resynchronization discards accumulated audio; its roughly 500-ms drop period is not a permanent half-second playback buffer.
