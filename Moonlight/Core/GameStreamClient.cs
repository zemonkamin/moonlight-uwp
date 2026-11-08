using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MoonlightNative;

namespace Moonlight.Core
{
    public sealed class GameStreamClient
    {
        private readonly Host host;
        public GameStreamClient(Host host) { this.host = host; }
        public static string Value(XContainer xml, string name, string fallback = "") => xml.Descendants(name).FirstOrDefault()?.Value ?? fallback;
        public async Task<XDocument> QueryAsync(string endpoint, string parameters = "", bool secure = true, int timeout = 10000, string pin = null)
        {
            await IdentityStore.InitializeAsync();
            string path = "/" + endpoint + "?uniqueid=" + IdentityStore.UniqueId + "&uuid=" + Guid.NewGuid().ToString("N") + parameters;
            var text = await Task.Run(() => IdentityStore.Identity.Request(host.Address, secure ? host.HttpsPort : host.HttpPort,
                secure, path, pin ?? host.Certificate ?? "", timeout));
            var doc = XDocument.Parse(text);
            var code = (string)doc.Root.Attribute("status_code");
            if (code != "200") throw new InvalidOperationException("GameStream " + code + ": " + ((string)doc.Root.Attribute("status_message") ?? "request rejected"));
            return doc;
        }
        public async Task<XDocument> ServerInfoAsync(bool secure = false)
        {
            var info = await QueryAsync("serverinfo", secure: secure);
            host.Name = Value(info, "hostname", host.Address);
            host.Id = Value(info, "uniqueid", host.Id);
            host.Mac = Value(info, "mac", host.Mac);
            int https;
            if (int.TryParse(Value(info, "HttpsPort"), out https) && https > 0 && https <= 65535) host.HttpsPort = https;
            return info;
        }
        public async Task<List<Game>> AppsAsync()
        {
            var doc = await QueryAsync("applist");
            return doc.Descendants("App").Select(e => new Game {
                Id = int.Parse(Value(e, "ID")), Title = Value(e, "AppTitle"), Running = Value(e, "IsRunning") == "1"
            }).OrderBy(g => g.Title).ToList();
        }
        public async Task PairAsync(string pin, CancellationToken cancellation)
        {
            // A stored identity is a connection, not a request to register the same
            // certificate again. Sunshine rejects duplicate authorized identities.
            if (!string.IsNullOrEmpty(host.Certificate))
            {
                var pairedInfo = await ServerInfoAsync(true);
                if (Value(pairedInfo, "PairStatus") == "1") return;
                throw new InvalidOperationException("The host did not accept the saved pairing. Check the client entry in Sunshine.");
            }
            var info = await ServerInfoAsync();
            int major;
            if (!int.TryParse(Value(info, "appversion", "7.0").Split('.')[0], out major))
                throw new InvalidOperationException("The host returned an invalid GameStream version.");
            bool sha256 = major >= 7;
            var salt = Crypto.Random(16);
            var aes = Crypto.Hash(Crypto.Join(salt, Encoding.UTF8.GetBytes(pin)), sha256).Take(16).ToArray();
            string common = "&devicename=Moonlight-W10M&updateState=1";
            string serverCertificate = null;
            bool hostAccepted = false;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var first = await QueryAsync("pair", common + "&phrase=getservercert&salt=" + Crypto.Hex(salt) +
                    "&clientcert=" + Crypto.Hex(Encoding.UTF8.GetBytes(IdentityStore.Identity.Certificate)), false, 120000);
                Paired(first);
                var plainCertificate = Value(first, "plaincert");
                if (string.IsNullOrEmpty(plainCertificate))
                    throw new InvalidOperationException("Another pairing request is active on the host. Complete or cancel it in Sunshine, then try again.");
                serverCertificate = Encoding.UTF8.GetString(Crypto.Unhex(plainCertificate));
                // Parsing validates the certificate before it is used for TLS or a signature.
                var serverSignature = Identity.CertificateSignature(serverCertificate);
                cancellation.ThrowIfCancellationRequested();
                var challenge = Crypto.Random(16);
                var second = await QueryAsync("pair", common + "&clientchallenge=" + Crypto.Hex(Crypto.Aes(challenge, aes, true)), false);
                Paired(second);
                var response = Crypto.Aes(Crypto.Unhex(Value(second, "challengeresponse")), aes, false);
                int hashLength = sha256 ? 32 : 20;
                if (response.Length < hashLength + 16) throw new InvalidOperationException("The host returned an incomplete pairing response.");
                var serverHash = response.Take(hashLength).ToArray();
                var serverChallenge = response.Skip(hashLength).Take(16).ToArray();
                var secret = Crypto.Random(16);
                var hash = Crypto.Hash(Crypto.Join(serverChallenge, Identity.CertificateSignature(IdentityStore.Identity.Certificate), secret), sha256);
                var third = await QueryAsync("pair", common + "&serverchallengeresp=" + Crypto.Hex(Crypto.Aes(hash, aes, true)), false);
                Paired(third);
                var signedSecret = Crypto.Unhex(Value(third, "pairingsecret"));
                if (signedSecret.Length <= 16 || !Identity.Verify(serverCertificate, signedSecret.Take(16).ToArray(), signedSecret.Skip(16).ToArray()))
                    throw new InvalidOperationException("Could not verify the host signature.");
                if (!Crypto.Equal(serverHash, Crypto.Hash(Crypto.Join(challenge, serverSignature, signedSecret.Take(16).ToArray()), sha256)))
                    throw new InvalidOperationException("Incorrect pairing PIN.");
                cancellation.ThrowIfCancellationRequested();
                var fourth = await QueryAsync("pair", common + "&clientpairingsecret=" + Crypto.Hex(Crypto.Join(secret, IdentityStore.Identity.Sign(secret))), false);
                Paired(fourth);
                hostAccepted = true;
                // The host has accepted our proof. Preserve this identity even if
                // the following HTTPS confirmation loses its network connection.
                await Store.SavePairingAsync(host, serverCertificate);
                var final = await QueryAsync("pair", common + "&phrase=pairchallenge", pin: serverCertificate);
                Paired(final);
            }
            catch
            {
                if (!hostAccepted)
                    try { await QueryAsync("unpair", secure: false); } catch { }
                throw;
            }
        }
        private static void Paired(XDocument doc)
        {
            if (Value(doc, "paired") != "1") throw new InvalidOperationException("The PC rejected pairing. Check the PIN.");
        }
        public async Task QuitAsync()
        {
            await QueryAsync("cancel");
            if (Value(await ServerInfoAsync(true), "currentgame", "0") != "0")
                throw new InvalidOperationException("The host did not close the game. It may have been started by another client.");
        }
        public async Task<Session> LaunchAsync(Game game, Preferences settings)
        {
            var info = await ServerInfoAsync(true);
            if (Value(info, "PairStatus") != "1") throw new InvalidOperationException("Pair with this PC first.");
            int current;
            int.TryParse(Value(info, "currentgame", "0"), out current);
            if (current != 0 && current != game.Id) throw new InvalidOperationException("Another game is already running on the PC. Use Quit game on PC to close it first.");
            byte[] key = Crypto.Random(16), iv = new byte[16];
            Array.Copy(Crypto.Random(4), iv, 4); iv[0] &= 0x7f;
            uint keyId = ((uint)iv[0] << 24) | ((uint)iv[1] << 16) | ((uint)iv[2] << 8) | iv[3];
            string args = "&rikey=" + Crypto.Hex(key) + "&rikeyid=" + keyId + "&surroundAudioInfo=196610&localAudioPlayMode=0";
            args += "&appid=" + game.Id + "&mode=" + settings.Width + "x" + settings.Height + "x" + settings.Fps +
                "&additionalStates=1&sops=1&remoteControllersBitmap=0&gcmap=0&gcpersist=0";
            var launch = await QueryAsync(current == 0 ? "launch" : "resume", args + Stream.LaunchParameters());
            if (Value(launch, current == 0 ? "gamesession" : "resume", "0") == "0") throw new InvalidOperationException("Could not start the stream on the PC.");
            int codecs; int.TryParse(Value(info, "ServerCodecModeSupport", "1"), out codecs);
            return new Session { Key = key, Iv = iv, AppVersion = Value(info, "appversion"), GfeVersion = Value(info, "GfeVersion"),
                RtspUrl = Value(launch, "sessionUrl0"), CodecSupport = codecs };
        }
    }
    public sealed class Session
    {
        public byte[] Key, Iv;
        public string AppVersion, GfeVersion, RtspUrl;
        public int CodecSupport;
    }
}
