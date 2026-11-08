using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Globalization;

namespace Moonlight
{
    sealed partial class App : Application
    {
        public App()
        {
            ApplicationLanguages.PrimaryLanguageOverride = "en-US";
            InitializeComponent(); Suspending += OnSuspending;
            UnhandledException += (s, e) => Core.ClientLog.Write("Unhandled exception", e.Exception);
            Resuming += (s, e) => { var frame = Window.Current.Content as Frame; if (frame?.Content is PlayerPage) frame.Navigate(typeof(MainPage)); };
        }
        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null) Window.Current.Content = frame = new Frame();
            if (frame.Content == null) frame.Navigate(typeof(MainPage));
            Window.Current.Activate();
            if (e.Arguments == "--self-test")
            {
                Window.Current.Content = new ScrollViewer { Content = new TextBlock { Text = await Core.Diagnostics.RunAsync(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) } };
            }
        }
        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try { if ((Window.Current.Content as Frame)?.Content is PlayerPage player) await player.DisconnectAsync(); }
            finally { deferral.Complete(); }
        }
    }
}
