#pragma once
#include <collection.h>

namespace MoonlightNative {
    public delegate int VideoFrame(const Platform::Array<unsigned char>^ bytes, long long timestampUs, bool keyFrame);
    public delegate void AudioFrame(const Platform::Array<unsigned char>^ pcm);
    public delegate void ConnectionMessage(Platform::String^ message, int code);

    public ref class Identity sealed {
    public:
        Identity(Platform::String^ certificate, Platform::String^ privateKey);
        static Identity^ Create();
        property Platform::String^ Certificate;
        property Platform::String^ PrivateKey;
        Platform::Array<unsigned char>^ Sign(const Platform::Array<unsigned char>^ data);
        static bool Verify(Platform::String^ certificate, const Platform::Array<unsigned char>^ data, const Platform::Array<unsigned char>^ signature);
        static Platform::Array<unsigned char>^ CertificateSignature(Platform::String^ certificate);
        // Blocking, bounded request; call from a worker thread. TLS requires an exact paired certificate.
        Platform::String^ Request(Platform::String^ host, int port, bool tls, Platform::String^ path, Platform::String^ pinnedCertificate, int timeoutMs);
        Platform::Array<unsigned char>^ RequestBytes(Platform::String^ host, int port, bool tls, Platform::String^ path, Platform::String^ pinnedCertificate, int timeoutMs);
    };

    public ref class Stream sealed {
    public:
        static int Start(Platform::String^ host, Platform::String^ appVersion, Platform::String^ gfeVersion,
            Platform::String^ sessionUrl, int codecSupport, int width, int height, int fps, int bitrate, bool remote,
            const Platform::Array<unsigned char>^ key, const Platform::Array<unsigned char>^ iv,
            VideoFrame^ video, AudioFrame^ audio, ConnectionMessage^ message);
        static void Interrupt();
        static void Stop();
        static void MouseMove(int x, int y);
        static void MousePosition(int x, int y, int width, int height);
        static void MouseButton(int button, bool down);
        static void Scroll(int clicks);
        static void Keyboard(int key, bool down, int modifiers);
        static void Text(Platform::String^ text);
        static Platform::String^ LaunchParameters();
        static Platform::String^ SelfTest();
    };
}
