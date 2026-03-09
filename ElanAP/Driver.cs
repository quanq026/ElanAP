using System;
using System.Runtime.InteropServices;
using System.Security;
using ElanAP.Devices;

namespace ElanAP
{
    class Driver
    {
        #region Win32 Mouse Hook

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll"), SuppressUnmanagedCodeSecurity]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll"), SuppressUnmanagedCodeSecurity]
        private static extern bool SetCursorPos(int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int x;
            public int y;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const uint LLMHF_INJECTED = 0x01;

        private LowLevelMouseProc _mouseProc;
        private IntPtr _hookId = IntPtr.Zero;

        private void InstallMouseHook()
        {
            _mouseProc = MouseHookCallback;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
            if (_hookId == IntPtr.Zero)
                FireOutput("Warning: Failed to install mouse hook. Error: " + Marshal.GetLastWin32Error());
            else
                FireOutput("Mouse hook installed - touchpad relative movement blocked.");
        }

        private void UninstallMouseHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                FireOutput("Mouse hook removed.");
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == WM_MOUSEMOVE)
            {
                // Allow injected/synthetic moves (our own SetCursorPos), block hardware moves
                var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                if ((info.flags & LLMHF_INJECTED) == 0)
                    return (IntPtr)1;
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        #endregion
        public Driver(API api)
        {
            API = api;
        }

        public Driver(API api, Area screen, Area touchpad) : this(api)
        {
            ScreenArea = screen;
            TouchpadArea = touchpad;
        }

        public Driver(API api, Area screen, Area touchpad, Touchpad device) : this(api, screen, touchpad)
        {
            TouchpadDevice = device;
        }

        public event EventHandler<string> Output;
        public event EventHandler<string> Status;

        public bool IsActive { get; private set; }

        private API API;

        public Area ScreenArea;
        public Area TouchpadArea;
        public Touchpad TouchpadDevice;

        private double ScaleX { get; set; }
        private double ScaleY { get; set; }

        // Pre-computed origin offsets (cached at Start to avoid property lookups per frame)
        private double _originX;
        private double _originY;
        private double _areaW;
        private double _areaH;
        private double _screenOffX;
        private double _screenOffY;

        #region Methods

        public void Start(IntPtr hwnd)
        {
            if (API.IsAvailable)
            {
                FireOutput("Starting...");

                ScaleX = ScreenArea.Width / TouchpadArea.Width;
                ScaleY = ScreenArea.Height / TouchpadArea.Height;

                // Cache all hot-path values as primitives
                _originX = TouchpadDevice.X_Lo + TouchpadArea.Position.X;
                _originY = TouchpadDevice.Y_Lo + TouchpadArea.Position.Y;
                _areaW = TouchpadArea.Width;
                _areaH = TouchpadArea.Height;
                _screenOffX = ScreenArea.Position.X;
                _screenOffY = ScreenArea.Position.Y;

                FireOutput("ScaleX,ScaleY:" + ScaleX + "," + ScaleY);
                FireOutput("Device Bounds: " + TouchpadDevice);

                API.OnTouch += HandleTouch;
                if (!API.StartReading(hwnd))
                {
                    API.OnTouch -= HandleTouch;
                    FireOutput("Failed to start reading.");
                    return;
                }

                InstallMouseHook();
                IsActive = true;
                FireOutput("Device hooked. Press F6 to toggle on/off.");
            }
        }

        public void Stop()
        {
            UninstallMouseHook();
            API.OnTouch -= HandleTouch;
            API.StopReading();
            FireOutput("Stopped.");
            IsActive = false;
        }

        private void HandleTouch(int rawX, int rawY)
        {
            double relX = rawX - _originX;
            double relY = rawY - _originY;

            if (relX < 0 || relX > _areaW || relY < 0 || relY > _areaH)
                return;

            SetCursorPos(
                (int)(relX * ScaleX + _screenOffX),
                (int)(relY * ScaleY + _screenOffY));
        }

        public void RefreshCache()
        {
            ScaleX = ScreenArea.Width / TouchpadArea.Width;
            ScaleY = ScreenArea.Height / TouchpadArea.Height;
            _originX = TouchpadDevice.X_Lo + TouchpadArea.Position.X;
            _originY = TouchpadDevice.Y_Lo + TouchpadArea.Position.Y;
            _areaW = TouchpadArea.Width;
            _areaH = TouchpadArea.Height;
            _screenOffX = ScreenArea.Position.X;
            _screenOffY = ScreenArea.Position.Y;
        }

        private void FireOutput(string msg)
        {
            var h = Output;
            if (h != null) h(this, msg);
        }

        #endregion
    }
}
