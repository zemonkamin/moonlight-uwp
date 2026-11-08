using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MoonlightNative;
using Windows.Media.Core;
using Windows.Storage;
using Windows.System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Moonlight.Core
{
    // Invoked only by the --self-test launch argument; runs in the actual UWP AppContainer.
    public static class Diagnostics
    {
        private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
        public static async Task<string> RunAsync()
        {
            var report = new List<string>();
            try
            {
                report.Add(await Task.Run(() => MoonlightNative.Stream.SelfTest()));
                var identity = await Task.Run(() => Identity.Create());
                var data = Encoding.UTF8.GetBytes("Moonlight W10M pairing test");
                var signature = identity.Sign(data);
                Require(Identity.Verify(identity.Certificate, data, signature), "RSA signature rejected");
                data[0] ^= 1;
                Require(!Identity.Verify(identity.Certificate, data, signature), "Tampered RSA message accepted");
                var reloaded = new Identity(identity.Certificate, identity.PrivateKey);
                Require(Identity.CertificateSignature(reloaded.Certificate).Length == 256, "Certificate signature length");
                report.Add("RSA-2048 / SHA-256 signing, tamper rejection, PEM reload: PASS");
                var key = Crypto.Unhex("000102030405060708090a0b0c0d0e0f");
                var clear = Crypto.Unhex("00112233445566778899aabbccddeeff");
                var cipher = Crypto.Aes(clear, key, true);
                Require(Crypto.Hex(cipher) == "69c4e0d86a7b0430d8cdb78070b4c55a", "NIST AES-128 vector");
                Require(Crypto.Equal(clear, Crypto.Aes(cipher, key, false)), "AES decrypt");
                report.Add("NIST AES-128 ECB vector: PASS");
                var dns = MakeDnsReply();
                var hosts = Discovery.Parse(dns, "192.168.1.9");
                Require(hosts.Count == 1 && hosts[0].Address == "192.168.1.9" && hosts[0].HttpPort == 47989, "DNS-SD response");
                bool rejected = false;
                try { Discovery.Parse(dns.Take(dns.Length - 1).ToArray(), "192.168.1.9"); } catch (FormatException) { rejected = true; }
                Require(rejected, "Truncated DNS packet accepted");
                report.Add("DNS-SD response and truncated packet rejection: PASS");
                using (var media = new StreamMedia(new Preferences()))
                {
                    Require(media.Video(new byte[] { 1 }, 0, false) == -1, "P-frame accepted before IDR");
                    Require(media.Video(new byte[] { 1 }, 1, true) == 0, "IDR rejected");
                    for (int i = 0; i < 3; i++) Require(media.Video(new byte[] { 1 }, i + 2, false) == 0, "Frame queue");
                    Require(media.Video(new byte[] { 1 }, 5, false) == -1, "Unbounded video queue");
                    Require(media.Video(new byte[] { 1 }, 6, false) == -1, "Broken reference chain accepted");
                    Require(media.Video(new byte[] { 1 }, 7, true) == 0, "IDR recovery failed");
                }
                var queue = new SampleQueue(); var pending = queue.TakeAsync(); queue.Close();
                Require(await pending == null, "Closed queue blocks decoder shutdown");
                report.Add("Video overflow / IDR recovery / pending sample cancellation: PASS");
                await IdentityStore.InitializeAsync();
                Require(IdentityStore.UniqueId.Length == 16, "Persistent client ID");
                var identityFile = await ApplicationData.Current.LocalFolder.GetFileAsync("identity.dat");
                var diskIdentity = await IdentityStore.ReadIdentityAsync(identityFile);
                Require(diskIdentity.Certificate == IdentityStore.Identity.Certificate, "Client certificate changed after disk reload");
                Require(Identity.Verify(IdentityStore.Identity.Certificate, data, diskIdentity.Sign(data)), "Private key changed after disk reload");
                report.Add("AppContainer protected identity persistence: PASS");
                var savedHosts = new List<Host> { new Host { Address = "192.0.2.1", Id = "persistence-test", Certificate = identity.Certificate } };
                const string testFile = "pairing-regression-test.json";
                try
                {
                    await Task.WhenAll(Store.WriteAsync(testFile, savedHosts), Store.WriteAsync(testFile, savedHosts));
                    var restoredHosts = await Store.ReadAsync<List<Host>>(testFile);
                    Require(restoredHosts.Count == 1 && restoredHosts[0].Certificate == identity.Certificate && restoredHosts[0].Id == "persistence-test", "Host pairing lost after reload");
                }
                finally { var test = await ApplicationData.Current.LocalFolder.TryGetItemAsync(testFile); if (test != null) await test.DeleteAsync(); }
                report.Add("Host certificate reload and concurrent atomic saves: PASS");
                var keyEvents = new List<Tuple<int, bool, int>>();
                var keyboard = new RemoteKeyboard((keyCode, down, modifiers) => keyEvents.Add(Tuple.Create(keyCode, down, modifiers)));
                keyboard.Press(VirtualKey.Shift, 0x2a, false, 1);
                keyboard.Press(VirtualKey.Shift, 0x36, false, 1);
                keyboard.Press(VirtualKey.Control, 0x1d, true, 2);
                keyboard.Press(VirtualKey.Menu, 0x38, true, 4);
                Require(keyEvents[0].Item1 == 0xa0 && keyEvents[1].Item1 == 0xa1 && keyEvents[2].Item1 == 0xa3 && keyEvents[3].Item1 == 0xa5, "Left/right keyboard modifier mapping");
                keyboard.Press(VirtualKey.Enter, 0x1c, false, 0);
                keyboard.Press(VirtualKey.Enter, 0x1c, true, 0);
                keyboard.Release(VirtualKey.Enter, 0x1c, false, 0);
                keyboard.Release(VirtualKey.Enter, 0x1c, true, 0);
                Require(keyEvents.Count(e => e.Item1 == 13 && !e.Item2) == 2 && keyEvents.Any(e => e.Item1 == 13 && !e.Item2 && (e.Item3 & 0x10) != 0), "Keypad Enter key-up lost");
                keyboard.Press(VirtualKey.Tab, 0x0f, false, 0);
                keyboard.Press(VirtualKey.A, 0x1e, false, 0);
                keyboard.Press(VirtualKey.A, 0x1e, false, 0);
                keyboard.ReleaseAll();
                Require(keyEvents.Count(e => e.Item1 == 65 && e.Item2) == 2 && keyEvents.Count(e => e.Item1 == 65 && !e.Item2) == 1, "Keyboard repeat or focus-loss release");
                int releasedCount = keyEvents.Count; keyboard.ReleaseAll();
                Require(keyEvents.Count == releasedCount, "Keyboard released twice after losing focus");
                report.Add("Physical keyboard modifiers, keypad Enter, repeat and focus-loss release: PASS");
                using (var legacy = new MemoryStream(Encoding.UTF8.GetBytes("{\"Width\":1280,\"Height\":720,\"Fps\":60,\"Bitrate\":8000,\"TouchController\":true}")))
                {
                    var preferences = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(legacy);
                    Require(preferences.NormalizeAfterLoad() && preferences.Fps == 30 && preferences.Bitrate == Preferences.MaximumBitrate, "Legacy stream settings were not migrated");
                    preferences.Bitrate = 12000;
                    Require(!preferences.NormalizeAfterLoad() && preferences.Bitrate == 12000, "Saved bitrate choice overwritten after migration");
                }
                Require(new Preferences().Fps == 30 && new Preferences().Bitrate == Preferences.MaximumBitrate, "Stream defaults");
                report.Add("30 FPS / maximum bitrate defaults and legacy settings migration: PASS");
                var iconFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Icon44.scale-100.png"));
                var cover = await ArtworkLoader.DecodeAsync((await FileIO.ReadBufferAsync(iconFile)).ToArray());
                Require(cover.PixelWidth > 0 && cover.PixelHeight > 0, "Artwork image decode failed");
                var card = new Game { Title = "Test game", Running = true };
                bool coverChanged = false;
                card.PropertyChanged += (s, args) => { if (args.PropertyName == "Cover") coverChanged = true; };
                card.Cover = cover;
                Require(coverChanged && card.Cover == cover && card.RunningVisibility == Windows.UI.Xaml.Visibility.Visible, "Artwork card binding did not update");
                report.Add("Binary artwork decode and card image binding: PASS");
                report.Add("RESULT: PASS");
            }
            catch (Exception ex) { report.Add("RESULT: FAIL\n" + ex); }
            string text = string.Join("\n", report);
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("self-test.txt", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, text);
            return text;
        }
        private static byte[] MakeDnsReply()
        {
            var bytes = new List<byte> { 0x4d, 0x4c, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0 };
            AddName(bytes, "TestPC._nvstream._tcp.local");
            bytes.AddRange(new byte[] { 0, 33, 0, 1, 0, 0, 0, 120, 0, 18, 0, 0, 0, 0, 0xbb, 0x75 });
            AddName(bytes, "host.local"); return bytes.ToArray();
        }
        private static void AddName(List<byte> data, string name)
        {
            foreach (var label in name.Split('.')) { data.Add((byte)label.Length); data.AddRange(Encoding.UTF8.GetBytes(label)); }
            data.Add(0);
        }
    }
}
