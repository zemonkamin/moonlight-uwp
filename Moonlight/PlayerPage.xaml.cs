using System;
using System.Threading;
using System.Threading.Tasks;
using Moonlight.Core;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.System;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Native = MoonlightNative.Stream;

namespace Moonlight
{
    public sealed partial class PlayerPage : Page
    {
        private StreamRequest request;
        private StreamMedia media;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly DisplayRequest display = new DisplayRequest();
        private readonly RemoteKeyboard keyboard = new RemoteKeyboard((key, down, modifiers) => Native.Keyboard(key, down, modifiers));
        private Task connectionTask, stoppingTask;
        private bool active, closed, displayHeld, relativeMouse, inputDialogOpen;
        private bool windowFocused = true;
        private bool inputPaused => inputDialogOpen || !windowFocused;
        private ContentDialog inputDialog;
        private int mouseButtons;
        private uint? touchPointer;
        private Point touchStart, touchLast;
        private DateTime touchTime;
        private bool touchMoved;
        private DisplayOrientations originalOrientation;
        private CoreCursor originalCursor;
        public PlayerPage() { InitializeComponent(); }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            request = (StreamRequest)e.Parameter;
            originalOrientation = DisplayInformation.AutoRotationPreferences;
            DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape | DisplayOrientations.LandscapeFlipped;
            ApplicationView.GetForCurrentView().TryEnterFullScreenMode();
            SystemNavigationManager.GetForCurrentView().BackRequested += Back;
            Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated += KeyboardActivated;
            Window.Current.Activated += Activated;
            MouseDevice.GetForCurrentView().MouseMoved += RelativeMove;
            originalCursor = Window.Current.CoreWindow.PointerCursor;
        }
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            SystemNavigationManager.GetForCurrentView().BackRequested -= Back;
            Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated -= KeyboardActivated;
            Window.Current.Activated -= Activated;
            MouseDevice.GetForCurrentView().MouseMoved -= RelativeMove;
            DisplayInformation.AutoRotationPreferences = originalOrientation;
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
            Window.Current.CoreWindow.PointerCursor = originalCursor;
        }
        private async void LoadedPlayer(object sender, RoutedEventArgs e)
        {
            if (connectionTask != null || closed) return;
            media = new StreamMedia(Store.Settings);
            Video.SetMediaStreamSource(media.Source);
            display.RequestActive(); displayHeld = true;
            connectionTask = ConnectAsync();
            try { await connectionTask; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) { Status.Text = ex.Message; ConnectionBar.Visibility = Visibility.Visible; await DisconnectAsync(); } }
        }
        private async Task ConnectAsync()
        {
            var session = await new GameStreamClient(request.Host).LaunchAsync(request.Game, Store.Settings);
            cancellation.Token.ThrowIfCancellationRequested();
            var settings = Store.Settings;
            int result = await Task.Run(() => Native.Start(request.Host.Address, session.AppVersion, session.GfeVersion, session.RtspUrl,
                session.CodecSupport, settings.Width, settings.Height, settings.Fps, settings.Bitrate, settings.Remote,
                session.Key, session.Iv, media.Video, media.Audio, Message));
            cancellation.Token.ThrowIfCancellationRequested();
            if (result != 0) throw new InvalidOperationException("Connection failed: " + result + ". Check the client log for details.");
            active = true;
            ConnectionBar.Visibility = Visibility.Collapsed;
            Focus(FocusState.Programmatic);
            Status.Text = request.Game.Title + " · " + settings.Width + "×" + settings.Height + " · " + settings.Fps + " FPS";
        }
        private async void Message(string message, int code)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => {
                if (closed) return;
                Status.Text = message;
                if (code != 0 && active) { ConnectionBar.Visibility = Visibility.Visible; await DisconnectAsync(); }
            });
        }
        public Task DisconnectAsync()
        {
            if (stoppingTask == null) stoppingTask = StopAsync();
            return stoppingTask;
        }
        private async Task StopAsync()
        {
            closed = true; cancellation.Cancel(); ReleaseInput(); active = false;
            inputDialog?.Hide();
            media?.Dispose(); Native.Interrupt();
            // Start and Stop are serialized. Interrupt alone is safe during LiStartConnection.
            if (connectionTask != null) { try { await connectionTask; } catch (Exception) { } }
            await Task.Run(() => Native.Stop());
            Video.Stop(); Video.Source = null;
            if (displayHeld) { display.RequestRelease(); displayHeld = false; }
        }
        private async void Disconnect(object sender, RoutedEventArgs e) { await RequestDisconnectAsync(); }
        private async void Back(object sender, BackRequestedEventArgs e)
        {
            e.Handled = true;
            await RequestDisconnectAsync();
        }
        private async Task RequestDisconnectAsync()
        {
            if (inputDialogOpen) return;
            if (active)
            {
                bool restoreRelativeMouse = relativeMouse;
                inputDialogOpen = true; ReleaseInput();
                var dialog = new ContentDialog {
                    Title = "Disconnect?", Content = "Do you want to disconnect from this PC? Your game will keep running on the PC.",
                    PrimaryButtonText = "Disconnect", SecondaryButtonText = "Cancel"
                };
                inputDialog = dialog;
                ContentDialogResult choice;
                try { choice = await dialog.ShowAsync(); }
                finally { inputDialog = null; inputDialogOpen = false; }
                if (choice != ContentDialogResult.Primary)
                {
                    if (active && !closed && restoreRelativeMouse)
                    {
                        relativeMouse = true; Window.Current.CoreWindow.PointerCursor = null;
                    }
                    Focus(FocusState.Programmatic);
                    return;
                }
            }
            await DisconnectAsync();
            if (Frame.CanGoBack) Frame.GoBack();
        }
        private async void PlaybackFailed(object sender, ExceptionRoutedEventArgs e)
        {
            Status.Text = "Video decoder error: " + e.ErrorMessage + ". Try 480p at 30 FPS.";
            ConnectionBar.Visibility = Visibility.Visible;
            await DisconnectAsync();
        }
        private void Activated(object sender, WindowActivatedEventArgs e)
        {
            windowFocused = e.WindowActivationState != CoreWindowActivationState.Deactivated;
            if (inputPaused) ReleaseInput();
        }
        private void ReleaseInput()
        {
            if (active)
            {
                keyboard.ReleaseAll();
                for (int button = 1; button <= 3; button++) Native.MouseButton(button, false);
            }
            mouseButtons = 0; touchPointer = null;
            relativeMouse = false;
            Window.Current.CoreWindow.PointerCursor = originalCursor;
        }
        private static int Modifiers(CoreWindow window)
        {
            int flags = 0;
            if ((window.GetKeyState(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0) flags |= 1;
            if ((window.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0) flags |= 2;
            if ((window.GetKeyState(VirtualKey.Menu) & CoreVirtualKeyStates.Down) != 0) flags |= 4;
            if (((window.GetKeyState(VirtualKey.LeftWindows) | window.GetKeyState(VirtualKey.RightWindows)) & CoreVirtualKeyStates.Down) != 0) flags |= 8;
            return flags;
        }
        private async void KeyboardActivated(CoreDispatcher dispatcher, AcceleratorKeyEventArgs e)
        {
            if (!active || inputPaused) return;
            bool down = e.EventType == CoreAcceleratorKeyEventType.KeyDown || e.EventType == CoreAcceleratorKeyEventType.SystemKeyDown;
            bool up = e.EventType == CoreAcceleratorKeyEventType.KeyUp || e.EventType == CoreAcceleratorKeyEventType.SystemKeyUp;
            if (!down && !up) return;
            var window = Window.Current.CoreWindow;
            int modifiers = Modifiers(window);
            e.Handled = true;
            if (down && (modifiers & 7) == 7 && e.VirtualKey == VirtualKey.Q) { await RequestDisconnectAsync(); return; }
            if (down && (modifiers & 7) == 7 && e.VirtualKey == VirtualKey.T) { await ShowTextInputAsync(); return; }
            if (down && (modifiers & 7) == 7 && e.VirtualKey == VirtualKey.M)
            {
                if (!e.KeyStatus.WasKeyDown)
                {
                    relativeMouse = !relativeMouse;
                    window.PointerCursor = relativeMouse ? null : originalCursor;
                }
                return;
            }
            if (down) keyboard.Press(e.VirtualKey, e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey, modifiers);
            else keyboard.Release(e.VirtualKey, e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey, modifiers);
        }
        private void RelativeMove(MouseDevice sender, MouseEventArgs e) { if (active && relativeMouse) Native.MouseMove(e.MouseDelta.X, e.MouseDelta.Y); }
        private void MouseState(PointerRoutedEventArgs e)
        {
            var p = e.GetCurrentPoint(InputSurface); var b = p.Properties;
            int current = (b.IsLeftButtonPressed ? 1 : 0) | (b.IsMiddleButtonPressed ? 2 : 0) | (b.IsRightButtonPressed ? 4 : 0);
            for (int i = 0; i < 3; i++) if (((mouseButtons ^ current) & (1 << i)) != 0) Native.MouseButton(i + 1, (current & (1 << i)) != 0);
            mouseButtons = current;
            if (!relativeMouse)
            {
                // Map the letterboxed video rectangle rather than the full phone screen.
                double scale = Math.Min(InputSurface.ActualWidth / Store.Settings.Width, InputSurface.ActualHeight / Store.Settings.Height);
                if (scale > 0)
                {
                    int x = (int)((p.Position.X - (InputSurface.ActualWidth - Store.Settings.Width * scale) / 2) / scale);
                    int y = (int)((p.Position.Y - (InputSurface.ActualHeight - Store.Settings.Height * scale) / 2) / scale);
                    Native.MousePosition(Math.Max(0, Math.Min(Store.Settings.Width - 1, x)), Math.Max(0, Math.Min(Store.Settings.Height - 1, y)), Store.Settings.Width, Store.Settings.Height);
                }
            }
        }
        private void PointerDown(object sender, PointerRoutedEventArgs e)
        {
            if (!active || inputPaused) return;
            InputSurface.CapturePointer(e.Pointer);
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse) MouseState(e);
            else if (touchPointer == null)
            {
                touchPointer = e.Pointer.PointerId; touchStart = touchLast = e.GetCurrentPoint(InputSurface).Position;
                touchTime = DateTime.UtcNow; touchMoved = false;
            }
            e.Handled = true;
        }
        private void PointerMove(object sender, PointerRoutedEventArgs e)
        {
            if (!active || inputPaused) return;
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse) MouseState(e);
            else if (touchPointer == e.Pointer.PointerId)
            {
                var p = e.GetCurrentPoint(InputSurface).Position;
                if (Math.Abs(p.X - touchStart.X) + Math.Abs(p.Y - touchStart.Y) > 5) touchMoved = true;
                Native.MouseMove((int)((p.X - touchLast.X) * 1.5), (int)((p.Y - touchLast.Y) * 1.5)); touchLast = p;
            }
            e.Handled = true;
        }
        private async void PointerUp(object sender, PointerRoutedEventArgs e)
        {
            if (!active || inputPaused) return;
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse) MouseState(e);
            else if (touchPointer == e.Pointer.PointerId)
            {
                touchPointer = null;
                if (!touchMoved)
                {
                    int button = (DateTime.UtcNow - touchTime).TotalMilliseconds > 500 ? 3 : 1;
                    Native.MouseButton(button, true); await Task.Delay(50);
                    if (active) Native.MouseButton(button, false);
                }
            }
            InputSurface.ReleasePointerCapture(e.Pointer); e.Handled = true;
        }
        private void PointerCancel(object sender, PointerRoutedEventArgs e)
        {
            if (touchPointer == e.Pointer.PointerId) touchPointer = null;
            if (active && mouseButtons != 0) { for (int i = 1; i <= 3; i++) Native.MouseButton(i, false); mouseButtons = 0; }
        }
        private void PointerWheel(object sender, PointerRoutedEventArgs e) { if (active && !inputPaused) Native.Scroll(e.GetCurrentPoint(InputSurface).Properties.MouseWheelDelta / 120); e.Handled = true; }
        private async Task ShowTextInputAsync()
        {
            if (!active || inputDialogOpen) return; ReleaseInput(); inputDialogOpen = true;
            var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = "Text to send to your PC" };
            var dialog = new ContentDialog { Title = "Send text", Content = text, PrimaryButtonText = "Send", SecondaryButtonText = "Cancel" };
            inputDialog = dialog;
            try { if (await dialog.ShowAsync() == ContentDialogResult.Primary && active) Native.Text(text.Text); }
            finally { inputDialog = null; inputDialogOpen = false; Focus(FocusState.Programmatic); }
        }
    }
}
