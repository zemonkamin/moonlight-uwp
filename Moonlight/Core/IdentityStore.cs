using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MoonlightNative;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;

namespace Moonlight.Core
{
    public static class IdentityStore
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1);
        public static Identity Identity { get; private set; }
        public static string UniqueId
        {
            get
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                if (!values.ContainsKey("uniqueid")) values["uniqueid"] = Crypto.Hex(Crypto.Random(8));
                return (string)values["uniqueid"];
            }
        }
        public static async Task InitializeAsync()
        {
            await Gate.WaitAsync();
            try
            {
                if (Identity != null) return;
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.TryGetItemAsync("identity.dat") as StorageFile;
                if (file != null)
                {
                    Identity = await ReadIdentityAsync(file);
                }
                else
                {
                    var identity = await Task.Run(() => Identity.Create());
                    var buffer = Encoding.UTF8.GetBytes(identity.Certificate + identity.PrivateKey).AsBuffer();
                    var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(buffer);
                    file = await folder.CreateFileAsync("identity.dat", CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteBufferAsync(file, encrypted);
                    Identity = identity;
                }
            }
            finally { Gate.Release(); }
        }
        internal static async Task<Identity> ReadIdentityAsync(StorageFile file)
        {
            var clear = await new DataProtectionProvider().UnprotectAsync(await FileIO.ReadBufferAsync(file));
            var text = Encoding.UTF8.GetString(clear.ToArray());
            var split = text.IndexOf("-----BEGIN RSA PRIVATE KEY-----", StringComparison.Ordinal);
            if (split < 0) throw new InvalidOperationException("The saved client identity is damaged.");
            return new Identity(text.Substring(0, split), text.Substring(split));
        }
    }
}
