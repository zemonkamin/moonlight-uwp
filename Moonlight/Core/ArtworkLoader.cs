using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace Moonlight.Core
{
    internal static class ArtworkLoader
    {
        private static readonly SemaphoreSlim Downloads = new SemaphoreSlim(3);
        public static Task LoadAsync(Host host, IEnumerable<Game> games, CancellationToken cancellation)
        {
            return Task.WhenAll(games.Select(game => LoadOneAsync(host, game, cancellation)));
        }
        private static async Task LoadOneAsync(Host host, Game game, CancellationToken cancellation)
        {
            bool entered = false;
            try
            {
                await Downloads.WaitAsync(cancellation); entered = true;
                cancellation.ThrowIfCancellationRequested();
                var folder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Artwork", CreationCollisionOption.OpenIfExists);
                string hostKey = Crypto.Hex(Crypto.Hash(Encoding.UTF8.GetBytes(host.Id ?? (host.Address + ":" + host.HttpPort)), true));
                string name = hostKey + "-" + game.Id + ".img";
                var cached = await folder.TryGetItemAsync(name) as StorageFile;
                if (cached != null)
                {
                    try
                    {
                        using (var stream = await cached.OpenReadAsync())
                        {
                            var image = new BitmapImage { DecodePixelWidth = 280 };
                            await image.SetSourceAsync(stream);
                            cancellation.ThrowIfCancellationRequested(); game.Cover = image; return;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { await cached.DeleteAsync(); }
                }
                await IdentityStore.InitializeAsync();
                cancellation.ThrowIfCancellationRequested();
                string path = "/appasset?uniqueid=" + IdentityStore.UniqueId + "&uuid=" + Guid.NewGuid().ToString("N") +
                    "&appid=" + game.Id + "&AssetType=2&AssetIdx=0";
                var bytes = await Task.Run(() => IdentityStore.Identity.RequestBytes(host.Address, host.HttpsPort, true, path, host.Certificate ?? "", 5000));
                cancellation.ThrowIfCancellationRequested();
                var cover = await DecodeAsync(bytes);
                cancellation.ThrowIfCancellationRequested(); game.Cover = cover;
                var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(file, bytes);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Keep the card's placeholder if the host has no usable cover. */ }
            finally { if (entered) Downloads.Release(); }
        }
        internal static async Task<BitmapImage> DecodeAsync(byte[] bytes)
        {
            using (var stream = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(stream))
                {
                    writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream();
                }
                stream.Seek(0);
                var image = new BitmapImage { DecodePixelWidth = 280 };
                await image.SetSourceAsync(stream);
                return image;
            }
        }
    }
}
