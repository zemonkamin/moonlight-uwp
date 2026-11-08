using System;
using Moonlight.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace Moonlight
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent(); var p = Store.Settings;
            Resolution.SelectedIndex = p.Height == 480 ? 0 : p.Height == 1080 ? 2 : 1;
            Bitrate.Value = p.Bitrate / 1000.0; Remote.IsOn = p.Remote;
        }
        private void BitrateChanged(object sender, RangeBaseValueChangedEventArgs e) { if (BitrateLabel != null) BitrateLabel.Text = (int)e.NewValue + " Mbps"; }
        private async void Save(object sender, RoutedEventArgs e)
        {
            var p = Store.Settings;
            p.Width = Resolution.SelectedIndex == 0 ? 854 : Resolution.SelectedIndex == 2 ? 1920 : 1280;
            p.Height = Resolution.SelectedIndex == 0 ? 480 : Resolution.SelectedIndex == 2 ? 1080 : 720;
            p.Bitrate = (int)Bitrate.Value * 1000;
            p.Remote = Remote.IsOn;
            try { await Store.SaveSettingsAsync(); Frame.GoBack(); }
            catch (Exception ex) { await new ContentDialog { Title = "Could not save settings", Content = ex.Message, SecondaryButtonText = "Close" }.ShowAsync(); }
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Visible;
            SystemNavigationManager.GetForCurrentView().BackRequested += Back;
        }
        protected override void OnNavigatedFrom(NavigationEventArgs e) { SystemNavigationManager.GetForCurrentView().BackRequested -= Back; }
        private void Back(object sender, BackRequestedEventArgs e) { e.Handled = true; if (Frame.CanGoBack) Frame.GoBack(); }
    }
}
