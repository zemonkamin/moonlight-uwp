using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moonlight.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI.Xaml.Navigation;

namespace Moonlight
{
    public sealed partial class MainPage : Page
    {
        private const double CardSlotWidth = 144;
        private bool ready, busy;
        private CancellationTokenSource artworkCancellation;
        private ItemsWrapGrid cardsPanel;
        public MainPage() { InitializeComponent(); }
        private Host Selected => HostList.SelectedItem as Host;
        private void AppListSizeChanged(object sender, SizeChangedEventArgs e) { UpdateContentWidths(); }
        private void CardsPanelLoaded(object sender, RoutedEventArgs e)
        {
            cardsPanel = (ItemsWrapGrid)sender;
            UpdateContentWidths();
        }
        private void UpdateContentWidths()
        {
            // The header and cards share the GridView's scroll viewer. Keep both centered,
            // while giving the card panel exactly enough width for complete columns.
            double available = Math.Max(0, AppList.ActualWidth - AppList.Padding.Left - AppList.Padding.Right);
            if (available <= 0) return;
            HostControls.Width = Math.Min(600, available);
            if (cardsPanel != null)
            {
                int columns = Math.Max(1, (int)(available / CardSlotWidth));
                cardsPanel.Width = Math.Min(available, columns * CardSlotWidth);
            }
        }
        private async void PageLoaded(object sender, RoutedEventArgs e)
        {
            if (ready) return;
            await Work(async () => { await Store.LoadAsync(); await IdentityStore.InitializeAsync(); Reload(); ready = true; UpdatePairingState(); });
        }
        private void Reload(Host selected = null)
        {
            HostList.ItemsSource = null; HostList.ItemsSource = Store.Hosts;
            HostList.SelectedItem = selected ?? Store.Hosts.FirstOrDefault();
        }
        private async Task Work(Func<Task> action)
        {
            if (busy) return; busy = true; Progress.Visibility = Visibility.Visible;
            try { await action(); }
            catch (Exception ex)
            {
                ClientLog.Write("Main page action", ex); Status.Text = ex.Message;
                await new ContentDialog { Title = "Action failed", Content = ex.Message, SecondaryButtonText = "Close" }.ShowAsync();
            }
            finally { busy = false; Progress.Visibility = Visibility.Collapsed; }
        }
        private async void AddHost(object sender, RoutedEventArgs e)
        {
            var address = new TextBox { Header = "PC IP address or hostname", PlaceholderText = "192.168.1.100" };
            var port = new TextBox { Header = "HTTP port", Text = "47989", InputScope = new Windows.UI.Xaml.Input.InputScope { Names = { new Windows.UI.Xaml.Input.InputScopeName(Windows.UI.Xaml.Input.InputScopeNameValue.Number) } } };
            var panel = new StackPanel(); panel.Children.Add(address); panel.Children.Add(port);
            var dialog = new ContentDialog { Title = "Add a PC", Content = panel, PrimaryButtonText = "Add", SecondaryButtonText = "Cancel" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await Work(async () => {
                int number; if (!int.TryParse(port.Text, out number) || number < 6 || number > 65535) throw new InvalidOperationException("Invalid port number.");
                var host = new Host { Address = address.Text.Trim().Trim('[', ']'), HttpPort = number, HttpsPort = number - 5 };
                if (string.IsNullOrWhiteSpace(host.Address) || host.Address.Contains("://") || host.Address.Contains("/")) throw new InvalidOperationException("Enter an IP address or hostname without http:// or a path.");
                await new GameStreamClient(host).ServerInfoAsync();
                var existing = Store.Hosts.FirstOrDefault(h => (!string.IsNullOrEmpty(host.Id) && h.Id == host.Id) || (h.Address == host.Address && h.HttpPort == host.HttpPort));
                if (existing == null) Store.Hosts.Add(host); else { existing.Address = host.Address; existing.HttpPort = host.HttpPort; existing.HttpsPort = host.HttpsPort; host = existing; }
                await Store.SaveHostsAsync(); Reload(host); UpdatePairingState();
            });
        }
        private async void FindHosts(object sender, RoutedEventArgs e)
        {
            await Work(async () => {
                Status.Text = "Searching for GameStream / Sunshine PCs on your network…";
                var hosts = await Discovery.FindAsync();
                foreach (var host in hosts)
                    if (!Store.Hosts.Any(h => h.Address == host.Address && h.HttpPort == host.HttpPort)) Store.Hosts.Add(host);
                await Store.SaveHostsAsync(); Reload(); Status.Text = "PCs found: " + hosts.Count + ". If your PC is missing, add it by IP address.";
            });
        }
        private void HostChanged(object sender, SelectionChangedEventArgs e)
        {
            artworkCancellation?.Cancel();
            AppList.ItemsSource = null;
            UpdatePairingState();
        }
        private void UpdatePairingState()
        {
            bool paired = !string.IsNullOrEmpty(Selected?.Certificate);
            PairButton.Content = paired ? "Connect" : "Pair";
            if (Selected != null) Status.Text = paired ? "Pairing saved. Select Connect to load your games." : "Pair with this PC to continue.";
        }
        private async void PairHost(object sender, RoutedEventArgs e)
        {
            if (Selected == null || busy) return;
            var host = Selected;
            await Work(async () => {
                if (!string.IsNullOrEmpty(host.Certificate))
                {
                    await LoadGamesAsync(host);
                    Status.Text = "Connected. Select a game to play.";
                    return;
                }
                // Check reachability before displaying a PIN the host cannot receive.
                await new GameStreamClient(host).ServerInfoAsync();
                string pin = (BitConverter.ToUInt32(Crypto.Random(4), 0) % 10000).ToString("D4");
                using (var cancel = new CancellationTokenSource())
                {
                    var dialog = new ContentDialog { Title = "PIN: " + pin, Content = "Enter this PIN in Sunshine or in the pairing prompt on your PC. Waiting for up to two minutes.", SecondaryButtonText = "Cancel" };
                    var shown = dialog.ShowAsync().AsTask();
                    TypedEventHandler<ContentDialog, ContentDialogClosedEventArgs> closed = (s, args) => cancel.Cancel();
                    dialog.Closed += closed;
                    try { await new GameStreamClient(host).PairAsync(pin, cancel.Token); Status.Text = "Pairing complete."; }
                    catch (OperationCanceledException) { Status.Text = "Pairing canceled."; }
                    catch (Exception ex)
                    {
                        ClientLog.Write("Pairing", ex); Status.Text = ex.Message;
                        dialog.Title = "Pairing failed";
                        dialog.Content = ex.Message + "\n\nClose this dialog, check your host, and try pairing again.";
                        dialog.SecondaryButtonText = "Close";
                        await shown;
                        return;
                    }
                    finally
                    {
                        // Closed may be delivered after ShowAsync completes on Mobile.
                        // Detach before disposing the token source to prevent a fatal callback.
                        dialog.Closed -= closed; dialog.Hide(); await shown;
                    }
                    if (cancel.IsCancellationRequested) return;
                }
                UpdatePairingState();
                await LoadGamesAsync(host);
            });
        }
        private async void RefreshApps(object sender, RoutedEventArgs e)
        {
            if (Selected == null) return; var host = Selected;
            await Work(async () => { await LoadGamesAsync(host); Status.Text = "Select a game to play."; });
        }
        private async Task LoadGamesAsync(Host host)
        {
            artworkCancellation?.Cancel();
            var lifetime = new CancellationTokenSource(); artworkCancellation = lifetime;
            var games = await new GameStreamClient(host).AppsAsync();
            if (lifetime.IsCancellationRequested || Selected != host) return;
            AppList.ItemsSource = games;
            LoadArtwork(host, games, lifetime.Token);
        }
        private async void LoadArtwork(Host host, System.Collections.Generic.List<Game> games, CancellationToken cancellation)
        {
            try { await ArtworkLoader.LoadAsync(host, games, cancellation); }
            catch (Exception ex) { ClientLog.Write("Loading artwork", ex); }
        }
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            artworkCancellation?.Cancel();
            base.OnNavigatedFrom(e);
        }
        private void PlayGame(object sender, ItemClickEventArgs e)
        {
            if (Selected != null && !busy) Frame.Navigate(typeof(PlayerPage), new StreamRequest { Host = Selected, Game = (Game)e.ClickedItem });
        }
        private void OpenSettings(object sender, RoutedEventArgs e) { if (!busy) Frame.Navigate(typeof(SettingsPage)); }
        private async void HostMenu(object sender, RoutedEventArgs e)
        {
            if (Selected == null || busy) return; var host = Selected;
            var wake = new Button { Content = "Wake PC (Wake-on-LAN)", HorizontalAlignment = HorizontalAlignment.Stretch };
            var quit = new Button { Content = "Quit game on PC", HorizontalAlignment = HorizontalAlignment.Stretch };
            var remove = new Button { Content = "Remove PC", HorizontalAlignment = HorizontalAlignment.Stretch };
            var panel = new StackPanel(); panel.Children.Add(wake); panel.Children.Add(quit); panel.Children.Add(remove);
            var dialog = new ContentDialog { Title = host.Name ?? host.Address, Content = panel, SecondaryButtonText = "Close" };
            bool delete = false, stop = false, awaken = false;
            wake.Click += (s, args) => { awaken = true; dialog.Hide(); };
            remove.Click += (s, args) => { delete = true; dialog.Hide(); };
            quit.Click += (s, args) => { stop = true; dialog.Hide(); };
            await dialog.ShowAsync();
            await Work(async () => {
                if (delete) { Store.Hosts.Remove(host); await Store.SaveHostsAsync(); Reload(); }
                if (stop) { await new GameStreamClient(host).QuitAsync(); Status.Text = "The game on your PC has been closed."; }
                if (awaken) { await WakeOnLan.SendAsync(host); Status.Text = "Wake-on-LAN packet sent. Wait for the PC to start, then select Refresh."; }
            });
        }
    }
}
