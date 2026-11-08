using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.MediaProperties;

namespace Moonlight.Core
{
    internal sealed class SampleQueue
    {
        private readonly Queue<MediaStreamSample> queue = new Queue<MediaStreamSample>();
        private TaskCompletionSource<MediaStreamSample> pending;
        private bool closed;
        public bool Add(MediaStreamSample sample, int limit, bool discardOld)
        {
            lock (queue)
            {
                if (closed) return false;
                if (pending != null) { var request = pending; pending = null; request.TrySetResult(sample); return true; }
                if (queue.Count >= limit)
                {
                    if (!discardOld) { queue.Clear(); return false; }
                    queue.Dequeue();
                }
                queue.Enqueue(sample); return true;
            }
        }
        public Task<MediaStreamSample> TakeAsync()
        {
            lock (queue)
            {
                if (queue.Count > 0) return Task.FromResult(queue.Dequeue());
                if (closed) return Task.FromResult<MediaStreamSample>(null);
                if (pending == null) pending = new TaskCompletionSource<MediaStreamSample>(TaskCreationOptions.RunContinuationsAsynchronously);
                return pending.Task;
            }
        }
        public void Close()
        {
            lock (queue) { closed = true; queue.Clear(); pending?.TrySetResult(null); pending = null; }
        }
    }
    public sealed class StreamMedia : IDisposable
    {
        private readonly SampleQueue video = new SampleQueue(), audio = new SampleQueue();
        private readonly VideoStreamDescriptor videoDescriptor;
        private long firstVideo = -1, lastVideo = -1, audioSamples;
        private readonly int fps;
        private bool needKeyFrame = true;
        public MediaStreamSource Source { get; }
        public long Frames { get; private set; }
        public long DroppedFrames { get; private set; }
        public StreamMedia(Preferences settings)
        {
            fps = settings.Fps;
            var properties = VideoEncodingProperties.CreateH264();
            properties.Width = (uint)settings.Width; properties.Height = (uint)settings.Height;
            properties.FrameRate.Numerator = (uint)fps; properties.FrameRate.Denominator = 1;
            videoDescriptor = new VideoStreamDescriptor(properties);
            Source = new MediaStreamSource(videoDescriptor, new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(48000, 2, 16))) {
                CanSeek = false, BufferTime = TimeSpan.Zero
            };
            Source.Starting += (s, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
            Source.SampleRequested += Requested;
            Source.Closed += (s, e) => Dispose();
        }
        public int Video(byte[] bytes, long timestampUs, bool keyFrame)
        {
            if (needKeyFrame && !keyFrame) { DroppedFrames++; return -1; }
            if (firstVideo < 0) firstVideo = timestampUs;
            long ticks = Math.Max(lastVideo + 1, (timestampUs - firstVideo) * 10);
            var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), TimeSpan.FromTicks(ticks));
            sample.Duration = TimeSpan.FromTicks(10000000 / fps); sample.KeyFrame = keyFrame;
            sample.Discontinuous = needKeyFrame;
            if (!video.Add(sample, 4, false)) { needKeyFrame = true; DroppedFrames++; return -1; }
            needKeyFrame = false; lastVideo = ticks; Frames++; return 0;
        }
        public void Audio(byte[] pcm)
        {
            var sample = MediaStreamSample.CreateFromBuffer(pcm.AsBuffer(), TimeSpan.FromTicks(audioSamples * 10000000 / 48000));
            sample.Duration = TimeSpan.FromTicks((pcm.Length / 4) * 10000000L / 48000);
            audioSamples += pcm.Length / 4;
            audio.Add(sample, 20, true);
        }
        private async void Requested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            try { args.Request.Sample = await (args.Request.StreamDescriptor == videoDescriptor ? video : audio).TakeAsync(); }
            catch (Exception) { args.Request.Sample = null; }
            finally { deferral.Complete(); }
        }
        public void Dispose() { video.Close(); audio.Close(); }
    }
}
