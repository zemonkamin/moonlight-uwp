using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using Windows.Storage;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace Moonlight.Core
{
    [DataContract]
    public sealed class Host
    {
        [DataMember] public string Address { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Id { get; set; }
        [DataMember] public int HttpPort { get; set; } = 47989;
        [DataMember] public int HttpsPort { get; set; } = 47984;
        [DataMember] public string Certificate { get; set; }
        [DataMember] public string Mac { get; set; }
        public override string ToString() => (Name ?? Address) + "  ·  " + Address;
    }
    public sealed class Game : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public bool Running { get; set; }
        private BitmapImage cover;
        public BitmapImage Cover
        {
            get => cover;
            internal set { cover = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cover))); }
        }
        public Visibility RunningVisibility => Running ? Visibility.Visible : Visibility.Collapsed;
        public event PropertyChangedEventHandler PropertyChanged;
        public override string ToString() => Title + (Running ? "  ▶" : "");
    }
    [DataContract]
    public sealed class Preferences
    {
        public const int MaximumBitrate = 40000;
        private const int CurrentVersion = 1;
        [DataMember] public int Width { get; set; } = 1280;
        [DataMember] public int Height { get; set; } = 720;
        public int Fps => 30;
        [DataMember] public int Bitrate { get; set; } = MaximumBitrate;
        [DataMember] public bool Remote { get; set; }
        [DataMember] public int SettingsVersion { get; set; }
        public bool NormalizeAfterLoad()
        {
            bool changed = SettingsVersion < CurrentVersion;
            if (changed) { Bitrate = MaximumBitrate; SettingsVersion = CurrentVersion; }
            int bitrate = Math.Max(2000, Math.Min(MaximumBitrate, Bitrate));
            changed |= bitrate != Bitrate; Bitrate = bitrate;
            return changed;
        }
    }
    public static class Store
    {
        private static readonly SemaphoreSlim LoadGate = new SemaphoreSlim(1);
        private static readonly SemaphoreSlim FileGate = new SemaphoreSlim(1);
        private static bool loaded;
        public static List<Host> Hosts { get; private set; } = new List<Host>();
        public static Preferences Settings { get; private set; } = new Preferences();
        public static async Task LoadAsync()
        {
            await LoadGate.WaitAsync();
            try
            {
                if (loaded) return;
                Hosts = await ReadAsync<List<Host>>("hosts.json") ?? new List<Host>();
                Settings = await ReadAsync<Preferences>("settings.json") ?? new Preferences();
                if (Settings.NormalizeAfterLoad()) await SaveSettingsAsync();
                loaded = true;
            }
            finally { LoadGate.Release(); }
        }
        internal static async Task<T> ReadAsync<T>(string name) where T : class
        {
            var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(name) as StorageFile;
            if (file == null) return null;
            try
            {
                var text = await FileIO.ReadTextAsync(file);
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch (SerializationException) { return null; }
        }
        public static async Task WriteAsync<T>(string name, T value)
        {
            await FileGate.WaitAsync();
            try
            {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                // Write then rename so suspension cannot leave a partially written JSON file.
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(name + ".tmp", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBytesAsync(file, stream.ToArray());
                await file.RenameAsync(name, NameCollisionOption.ReplaceExisting);
            }
            }
            finally { FileGate.Release(); }
        }
        public static async Task SavePairingAsync(Host host, string certificate)
        {
            await LoadAsync();
            var stored = Hosts.FirstOrDefault(h => ReferenceEquals(h, host) ||
                (!string.IsNullOrEmpty(host.Id) && h.Id == host.Id) ||
                (h.Address == host.Address && h.HttpPort == host.HttpPort));
            if (stored == null) { stored = host; Hosts.Add(stored); }
            stored.Certificate = certificate;
            host.Certificate = certificate;
            await SaveHostsAsync();
        }
        public static Task SaveHostsAsync() => WriteAsync("hosts.json", Hosts);
        public static Task SaveSettingsAsync() => WriteAsync("settings.json", Settings);
    }
    public sealed class StreamRequest
    {
        public Host Host { get; set; }
        public Game Game { get; set; }
    }
}
