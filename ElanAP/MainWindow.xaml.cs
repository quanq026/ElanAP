using ElanAP.Devices;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace ElanAP
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint VK_F6 = 0x75;
        private const int WM_HOTKEY = 0x0312;

        public MainWindow()
        {
            InitializeComponent();
        }

        private IntPtr _hwnd;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Console.Log("Window loaded.");
            Config = LoadDefaultConfig();

            _hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(_hwnd);
            source.AddHook(WndProc);

            NotifyIcon = new NotifyIcon();
            NotifyIcon.ShowWindow += NotifyIcon_ShowWindow;

            API = new API();
            API.Output += Console.Log;

            Driver = new Driver(API);
            Driver.Output += Console.Log;
            Driver.Status += StatusUpdate;

            Screen = new Screen();
            Touchpad = new Touchpad(API);

            DesktopRes = Screen.Bounds;
            TouchpadRes = Touchpad.Bounds;

            ScreenMapArea.BackgroundArea = DesktopRes;
            TouchpadMapArea.BackgroundArea = TouchpadRes;
            ScreenMapArea.ForegroundArea = Config.Screen;
            TouchpadMapArea.ForegroundArea = Config.Touchpad;

            ScreenMapArea.AreaDragged += OnScreenAreaDragged;
            TouchpadMapArea.AreaDragged += OnTouchpadAreaDragged;

            if (RegisterHotKey(_hwnd, HOTKEY_ID, 0, VK_F6))
                Console.Log("Global hotkey F6 registered for Start/Stop toggle.");
            else
                Console.Log("Warning: Failed to register F6 hotkey.");

            StartButton.IsEnabled = API.IsAvailable;
            if (!API.IsAvailable)
                Console.Log(API, "API is unavailable. Please ensure an Elan HID touchpad is connected.");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x00FF && API != null) // WM_INPUT
            {
                API.ProcessRawInput(lParam);
            }
            else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                if (Driver.IsActive)
                    StopDriverButton();
                else
                    StartDriverButton();
                handled = true;
            }
            return IntPtr.Zero;
        }

        #region Properties

        private Area DesktopRes { get; set; }
        private Area TouchpadRes { get; set; }

        private API API { get; set; }
        private Driver Driver { get; set; }

        private Screen Screen { get; set; }
        private Touchpad Touchpad { get; set; }

        private NotifyIcon NotifyIcon { get; set; }

        #endregion

        #region Main Buttons

        private void StartDriverButton(object sender = null, EventArgs e = null)
        {
            if (!Driver.IsActive)
            {
                Driver.ScreenArea = Config.Screen;
                Driver.TouchpadArea = Config.Touchpad;
                Driver.TouchpadDevice = Touchpad;
                Driver.Start(_hwnd);

                if (Driver.IsActive)
                {
                    StartButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                }
            }
        }

        private void StopDriverButton(object sender = null, EventArgs e = null)
        {
            if (Driver.IsActive)
            {
                Driver.Stop();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            }
        }

        #endregion

        #region Property Updates

        private void UpdateScreen(object sender = null, EventArgs e = null)
        {
            ScreenMapArea.ForegroundArea = Config.Screen;

            if (Driver.IsActive)
            {
                Driver.ScreenArea = Config.Screen;
                Driver.TouchpadArea = Config.Touchpad;
                Driver.RefreshCache();
            }
        }

        private void UpdateTouchpad(object sender = null, TextChangedEventArgs e = null)
        {
            TouchpadMapArea.ForegroundArea = Config.Touchpad;
            if (Config.LockAspectRatio && sender is TextBox)
            {
                var box = (TextBox)sender;
                var caret = box.CaretIndex;

                if (sender == TouchpadHeightBox)
                    Config.Touchpad.Width = Math.Round(Config.Screen.Width / Config.Screen.Height * Config.Touchpad.Height);
                else if (sender == TouchpadWidthBox)
                    Config.Touchpad.Height = Math.Round(Config.Screen.Height / Config.Screen.Width * Config.Touchpad.Width);

                box.CaretIndex = caret;
            }
        }

        private void CenterArea(object sender = null, EventArgs e = null)
        {
            string tag = (string)(sender as Control).Tag;
            CenterHorizontal(tag);
            CenterVertical(tag);
        }

        private void CenterHorizontal(object sender = null, EventArgs e = null)
        {
            CenterHorizontal((string)(sender as Control).Tag);
        }
        private void CenterHorizontal(string tag)
        {
            if (tag == "Screen")
                Config.Screen.Position.X = (Screen.Bounds.Width - Config.Screen.Width) / 2;
            if (tag == "Touchpad")
                Config.Touchpad.Position.X = (Touchpad.Bounds.Width - Config.Touchpad.Width) / 2;
        }

        private void CenterVertical(object sender = null, EventArgs e = null)
        {
            CenterVertical((string)(sender as Control).Tag);
        }
        private void CenterVertical(string tag)
        {
            if (tag == "Screen")
                Config.Screen.Position.Y = (Screen.Bounds.Height - Config.Screen.Height) / 2;
            if (tag == "Touchpad")
                Config.Touchpad.Position.Y = (Touchpad.Bounds.Height - Config.Touchpad.Height) / 2;
        }

        #endregion

        #region File Management

        public Configuration Config
        {
            set
            {
                _config = value;
                NotifyPropertyChanged();
            }
            get { return _config; }
        }
        private Configuration _config;

        private Configuration LoadDefaultConfig()
        {
            try
            {
                return Configuration.Read(Info.DefaultConfigPath);
            }
            catch
            {
                return new Configuration();
            }
        }

        public void SaveDefaultConfig()
        {
            Config.Save(Info.DefaultConfigPath);
        }

        private void LoadDialog(object sender = null, EventArgs e = null)
        {
            var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "ElanAP configuration files (*.cfg)|*.cfg|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                RestoreDirectory = true,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    Config = Configuration.Read(dialog.FileName);
                }
                catch
                {
                    Console.Log("Error: Invalid configuration file.");
                }
            }
        }

        private void SaveDialog(object sender = null, EventArgs e = null)
        {
            var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "ElanAP configuration files (*.cfg)|*.cfg|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                RestoreDirectory = true,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Config.Save(dialog.FileName);
            }
        }

        private void SaveDefaults(object sender = null, EventArgs e = null)
        {
            SaveDefaultConfig();
        }

        #endregion

        #region Misc.

        private void StatusUpdate(object sender, string e)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                Status.Text = e;
            }));
        }

        private async void Window_StateChanged(object sender, EventArgs e)
        {
            await Task.Delay(1);
            if (!IsLoaded) return;
            Window_SizeChanged();

            switch (WindowState)
            {
                case WindowState.Minimized:
                    {
                        ShowInTaskbar = false;
                        NotifyIcon.Visible = true;
                        Hide();
                        break;
                    }
                case WindowState.Normal:
                    {
                        ShowInTaskbar = true;
                        NotifyIcon.Visible = false;
                        break;
                    }
            }
        }

        private void Window_SizeChanged(object sender = null, EventArgs e = null)
        {
            if (IsLoaded)
            {
                ScreenMapArea.UpdateCanvas();
                TouchpadMapArea.UpdateCanvas();
            }
        }

        private void ShowAbout(object sender = null, EventArgs e = null)
        {
            new Windows.AboutBox().Show();
        }

        private void NotifyIcon_ShowWindow(object sender, EventArgs e)
        {
            this.Show();
            WindowState = WindowState.Normal;
        }

        private void ExitApp(object sender = null, EventArgs e = null)
        {
            Application.Current.Shutdown();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (Driver != null && Driver.IsActive)
                Driver.Stop();
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            if (NotifyIcon != null)
                NotifyIcon.Dispose();
        }

        #endregion

        #region Drag-to-Select

        private void OnScreenAreaDragged(object sender, Area newArea)
        {
            Config.Screen.Width = Math.Round(newArea.Width);
            Config.Screen.Height = Math.Round(newArea.Height);
            Config.Screen.Position.X = Math.Round(newArea.Position.X);
            Config.Screen.Position.Y = Math.Round(newArea.Position.Y);
            ScreenMapArea.ForegroundArea = Config.Screen;
            ScreenProfileCombo.SelectedIndex = 0; // Custom
        }

        private void OnTouchpadAreaDragged(object sender, Area newArea)
        {
            Config.Touchpad.Width = Math.Round(newArea.Width);
            Config.Touchpad.Height = Math.Round(newArea.Height);
            Config.Touchpad.Position.X = Math.Round(newArea.Position.X);
            Config.Touchpad.Position.Y = Math.Round(newArea.Position.Y);
            TouchpadMapArea.ForegroundArea = Config.Touchpad;
            ProfileCombo.SelectedIndex = 0; // Switch to Custom
        }

        #endregion

        #region Profiles

        private void ScreenProfileChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || ScreenProfileCombo.SelectedIndex <= 0) return;

            double sw = DesktopRes.Width;
            double sh = DesktopRes.Height;
            double w, h;

            switch (ScreenProfileCombo.SelectedIndex)
            {
                case 1:  w = sw;   h = sh;   break; // Full Screen
                case 2:  w = 1920; h = 1080; break;
                case 3:  w = 2560; h = 1440; break;
                case 4:  w = 3840; h = 2160; break;
                case 5:  w = 1680; h = 1050; break;
                case 6:  w = 1600; h = 900;  break;
                case 7:  w = 1440; h = 900;  break;
                case 8:  w = 1366; h = 768;  break;
                case 9:  w = 1280; h = 720;  break;
                case 10: w = 1280; h = 1024; break;
                case 11: w = 1024; h = 768;  break;
                default: return;
            }

            // Clamp to actual screen size
            if (w > sw) w = sw;
            if (h > sh) h = sh;

            Config.Screen.Width = w;
            Config.Screen.Height = h;
            // Center on screen
            Config.Screen.Position.X = Math.Round((sw - w) / 2);
            Config.Screen.Position.Y = Math.Round((sh - h) / 2);
            ScreenMapArea.ForegroundArea = Config.Screen;
        }

        private void ProfileChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || ProfileCombo.SelectedIndex <= 0) return;

            double tw = TouchpadRes.Width;
            double th = TouchpadRes.Height;
            double w, h, x, y;

            switch (ProfileCombo.SelectedIndex)
            {
                case 1: // Full Area
                    w = tw; h = th; x = 0; y = 0;
                    break;
                case 2: // Top Half
                    w = tw; h = th / 2; x = 0; y = 0;
                    break;
                case 3: // Bottom Half
                    w = tw; h = th / 2; x = 0; y = th / 2;
                    break;
                case 4: // Center 75%
                    w = tw * 0.75; h = th * 0.75; x = tw * 0.125; y = th * 0.125;
                    break;
                case 5: // Center 50%
                    w = tw * 0.5; h = th * 0.5; x = tw * 0.25; y = th * 0.25;
                    break;
                case 6: // Left Half
                    w = tw / 2; h = th; x = 0; y = 0;
                    break;
                case 7: // Right Half
                    w = tw / 2; h = th; x = tw / 2; y = 0;
                    break;
                default:
                    return;
            }

            Config.Touchpad.Width = Math.Round(w);
            Config.Touchpad.Height = Math.Round(h);
            Config.Touchpad.Position.X = Math.Round(x);
            Config.Touchpad.Position.Y = Math.Round(y);
            TouchpadMapArea.ForegroundArea = Config.Touchpad;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string PropertyName = "")
        {
            if (PropertyName != null)
            {
                var h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs(PropertyName));
            }
        }

        #endregion
    }
}
