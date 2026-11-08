using System;
using System.IO;
using Windows.Storage;

namespace Moonlight.Core
{
    public static class ClientLog
    {
        private static readonly object Gate = new object();
        public static void Write(string context, Exception error)
        {
            try
            {
                lock (Gate)
                {
                    var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "client.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + context + "\n" + error + "\n\n");
                }
            }
            catch { /* Reporting an error must never become a second failure. */ }
        }
    }
}
