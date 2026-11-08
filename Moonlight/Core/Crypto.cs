using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;

namespace Moonlight.Core
{
    public static class Crypto
    {
        public static byte[] Random(uint count) => CryptographicBuffer.GenerateRandom(count).ToArray();
        public static string Hex(byte[] bytes) => CryptographicBuffer.EncodeToHexString(bytes.AsBuffer());
        public static byte[] Unhex(string text) => CryptographicBuffer.DecodeFromHexString(text).ToArray();
        public static byte[] Join(params byte[][] arrays) => arrays.SelectMany(a => a).ToArray();
        public static byte[] Hash(byte[] bytes, bool sha256) => HashAlgorithmProvider.OpenAlgorithm(sha256 ? HashAlgorithmNames.Sha256 : HashAlgorithmNames.Sha1).HashData(bytes.AsBuffer()).ToArray();
        public static byte[] Aes(byte[] bytes, byte[] key, bool encrypt)
        {
            var algorithm = SymmetricKeyAlgorithmProvider.OpenAlgorithm(SymmetricAlgorithmNames.AesEcb);
            var cryptoKey = algorithm.CreateSymmetricKey(key.AsBuffer());
            if (encrypt)
            {
                var padded = new byte[(bytes.Length + 15) / 16 * 16];
                Array.Copy(bytes, padded, bytes.Length);
                return CryptographicEngine.Encrypt(cryptoKey, padded.AsBuffer(), null).ToArray();
            }
            if (bytes.Length % 16 != 0) throw new InvalidOperationException("Invalid pairing response.");
            return CryptographicEngine.Decrypt(cryptoKey, bytes.AsBuffer(), null).ToArray();
        }
        public static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
    }
}
