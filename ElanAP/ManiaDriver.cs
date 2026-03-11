using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using ElanAP.Devices;

namespace ElanAP
{
    class ManiaDriver
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
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const uint LLMHF_INJECTED = 0x01;

        private LowLevelMouseProc _mouseProc;
        private IntPtr _hookId = IntPtr.Zero;

        // Timestamp of last touchpad Raw Input — used to distinguish touchpad from USB mouse
        private volatile int _lastTouchpadInputTick;

        private void InstallMouseHook()
        {
            _mouseProc = MouseHookCallback;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
            if (_hookId == IntPtr.Zero)
                FireOutput("Warning: Failed to install mouse hook. Error: " + Marshal.GetLastWin32Error());
            else
                FireOutput("Mouse hook installed - touchpad cursor movement blocked.");
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

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = (int)wParam;
                    if (msg == WM_MOUSEMOVE || msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP ||
                        msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP ||
                        msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP ||
                        msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL ||
                        msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
                    {
                        var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                        // Allow injected events (from software)
                        if ((info.flags & LLMHF_INJECTED) != 0)
                            return CallNextHookEx(_hookId, nCode, wParam, lParam);

                        // Block only if touchpad was active recently (within 100ms)
                        // This allows USB/external mice to work normally
                        int elapsed = Environment.TickCount - _lastTouchpadInputTick;
                        if (elapsed >= 0 && elapsed < 100)
                            return (IntPtr)1;
                    }
                }
            }
            catch (Exception ex)
            {
                App.WriteLog("CRASH MouseHookCallback: " + ex);
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        #endregion

        #region Win32 Keyboard Hook

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint VK_LWIN = 0x5B;
        private const uint VK_RWIN = 0x5C;
        private const uint VK_TAB = 0x09;
        private const uint VK_MENU = 0x12;  // Alt key
        private const uint LLKHF_ALTDOWN = 0x20;
        private const uint LLKHF_INJECTED = 0x10;

        // Magic marker to identify our own SendInput calls vs PTP gesture injections
        private static readonly IntPtr EXTRAINFO_MARKER = (IntPtr)0x4D4E4941; // "MNIA"

        private LowLevelKeyboardProc _kbProc;
        private IntPtr _kbHookId = IntPtr.Zero;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private void InstallKeyboardHook()
        {
            _kbProc = KeyboardHookCallback;
            _kbHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, GetModuleHandle(null), 0);
            if (_kbHookId == IntPtr.Zero)
                FireOutput("Warning: Failed to install keyboard hook. Error: " + Marshal.GetLastWin32Error());
            else
                FireOutput("Keyboard hook installed - gesture shortcuts blocked.");
        }

        // SetWindowsHookEx is already imported above but accepts different delegate types via IntPtr
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookEx")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        private void UninstallKeyboardHook()
        {
            if (_kbHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_kbHookId);
                _kbHookId = IntPtr.Zero;
                FireOutput("Keyboard hook removed.");
            }
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));

                    if (info.vkCode == VK_LWIN || info.vkCode == VK_RWIN)
                        return (IntPtr)1;

                    if ((info.flags & LLKHF_INJECTED) != 0 && info.dwExtraInfo != EXTRAINFO_MARKER)
                        return (IntPtr)1;
                }
            }
            catch (Exception ex)
            {
                App.WriteLog("CRASH KeyboardHookCallback: " + ex);
            }
            return CallNextHookEx(_kbHookId, nCode, wParam, lParam);
        }

        #endregion

        #region Win32 SendInput

        [DllImport("user32.dll", SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        private const uint MAPVK_VK_TO_VSC = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        // Union must include MOUSEINPUT so the struct has the correct size
        // (MOUSEINPUT is the largest member, 32 bytes on x64)
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        #endregion

        #region Touchpad Gesture Suppression

        // Gestures are suppressed entirely through hooks (no registry changes needed):
        // - Mouse hook: blocks touchpad cursor movement and clicks
        // - Keyboard hook: blocks injected gesture shortcuts (3/4 finger swipe = Win+Tab etc.)
        // - Raw Input: touchpad data is consumed directly, bypassing gesture recognition
        // This approach is crash-safe — no persistent system changes that need cleanup.

        #endregion

        #region Win32 Dedicated Input Thread

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        private delegate IntPtr InputWndProcDel(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static readonly IntPtr HWND_MESSAGE_PARENT = (IntPtr)(-3);
        private const uint WM_INPUT_MSG = 0x00FF;
        private const uint WM_CLOSE_MSG = 0x0010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        private Thread _inputThread;
        private IntPtr _inputHwnd;
        private InputWndProcDel _inputWndProcDel; // prevent GC
        private ManualResetEvent _threadReadyEvent;
        private volatile bool _inputStartedOK;

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private IntPtr InputWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == WM_INPUT_MSG)
                {
                    _lastTouchpadInputTick = Environment.TickCount;
                    API.ProcessRawInput(lParam);
                    return IntPtr.Zero;
                }
                if (msg == WM_CLOSE_MSG)
                {
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                }
                return DefWindowProc(hWnd, msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                App.WriteLog("CRASH InputWndProc: " + ex);
                return DefWindowProc(hWnd, msg, wParam, lParam);
            }
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uMilliseconds);

        private void StartInputThread()
        {
            _inputStartedOK = false;
            _threadReadyEvent = new ManualResetEvent(false);
            timeBeginPeriod(1);
            _inputThread = new Thread(InputThreadProc);
            _inputThread.IsBackground = true;
            _inputThread.Priority = ThreadPriority.Highest;
            _inputThread.Start();
            _threadReadyEvent.WaitOne(5000);
            _threadReadyEvent.Close();
            _threadReadyEvent = null;
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void InputThreadProc()
        {
            try
            {
                // Create message-only window on this thread
                _inputWndProcDel = InputWndProc;
                var wc = new WNDCLASS();
                wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_inputWndProcDel);
                wc.hInstance = GetModuleHandle(null);
                wc.lpszClassName = "ElanAP_ManiaInput";
                RegisterClass(ref wc);

                _inputHwnd = CreateWindowEx(0, "ElanAP_ManiaInput", "", 0,
                    0, 0, 0, 0, HWND_MESSAGE_PARENT, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

                if (_inputHwnd == IntPtr.Zero)
                {
                    FireOutput("Failed to create input window. Error: " + Marshal.GetLastWin32Error());
                    _threadReadyEvent.Set();
                    return;
                }

                // Register Raw Input on this thread's window
                API.OnContactUpdate += HandleContactUpdate;
                API.OnFrameComplete += HandleFrameComplete;
                API.OnAllContactsLifted += HandleAllContactsLifted;
                if (!API.StartReading(_inputHwnd))
                {
                    API.OnContactUpdate -= HandleContactUpdate;
                    API.OnFrameComplete -= HandleFrameComplete;
                    API.OnAllContactsLifted -= HandleAllContactsLifted;
                    DestroyWindow(_inputHwnd);
                    _inputHwnd = IntPtr.Zero;
                    _threadReadyEvent.Set();
                    return;
                }

                // Install hooks on THIS thread (uses this thread's message pump)
                InstallMouseHook();
                InstallKeyboardHook();

                _inputStartedOK = true;
                _threadReadyEvent.Set();

                FireOutput("Dedicated input thread running. INPUT struct size: " + _inputStructSize + " bytes. Press F6 to toggle.");

                // Message pump - processes WM_INPUT + hook callbacks
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                App.WriteLog("INPUT THREAD CRASH: " + ex);
                FireOutput("Input thread error: " + ex.Message);
            }
            finally
            {
                // Cleanup on this thread
                UninstallKeyboardHook();
                UninstallMouseHook();
                API.OnContactUpdate -= HandleContactUpdate;
                API.OnFrameComplete -= HandleFrameComplete;
                API.OnAllContactsLifted -= HandleAllContactsLifted;
                API.StopReading();

                // Release all held keys
                _trackCount = 0;
                if (_activeZones != null)
                {
                    for (int i = 0; i < _activeZones.Length; i++)
                    {
                        if (_activeZones[i])
                            SendKey(_zoneVk[i], _zoneScan[i], true);
                        _activeZones[i] = false;
                    }
                }
                _activeZoneCount = 0;

                if (_inputHwnd != IntPtr.Zero)
                {
                    DestroyWindow(_inputHwnd);
                    _inputHwnd = IntPtr.Zero;
                }
                UnregisterClass("ElanAP_ManiaInput", GetModuleHandle(null));
            }
        }

        private void StopInputThread()
        {
            if (_inputHwnd != IntPtr.Zero)
                PostMessage(_inputHwnd, WM_CLOSE_MSG, IntPtr.Zero, IntPtr.Zero);
            if (_inputThread != null && _inputThread.IsAlive)
                _inputThread.Join(3000);
            _inputThread = null;
            timeEndPeriod(1);
        }

        #endregion

        public ManiaDriver(API api)
        {
            API = api;
        }

        public event EventHandler<string> Output;
        public event EventHandler<string> Status;
        public event Action<int[]> ActiveZonesChanged;

        public bool IsActive { get; private set; }

        private API API;
        public Touchpad TouchpadDevice;
        public List<Zone> Zones = new List<Zone>();

        // Track which zones are currently pressed (by zone index) - use bool array for O(1) lookup
        private bool[] _activeZones;
        private int _activeZoneCount;

        // Pre-computed zone bounds in device coordinates (use int for faster comparison)
        private int[] _zoneX1, _zoneY1, _zoneX2, _zoneY2;
        private ushort[] _zoneVk;
        private ushort[] _zoneScan; // pre-computed scan codes

        // Pre-allocated for SendInput (avoid per-call allocation)
        private INPUT[] _inputBuffer = new INPUT[1];
        private int _inputStructSize;

        // Pre-allocated for visual feedback
        private int[] _activeZoneIndices;
        private static readonly int[] _emptyZoneIndices = new int[0];

        // Performance tracking
        public bool PerfEnabled { get; set; }
        private bool _visualFeedbackEnabled = true;
        public bool VisualFeedbackEnabled
        {
            get { return _visualFeedbackEnabled; }
            set { _visualFeedbackEnabled = value; }
        }
        private Stopwatch _handlerWatch;
        private long _totalHandlerTicks;
        private long _minHandlerTicks;
        private long _maxHandlerTicks;
        private int _handlerCount;
        private long _lastStatTicks;

        public void Start(IntPtr hwnd)
        {
            if (!API.IsAvailable || Zones.Count == 0)
            {
                FireOutput("Cannot start: " + (API.IsAvailable ? "No zones configured." : "API unavailable."));
                return;
            }

            FireOutput("Mania mode starting with " + Zones.Count + " zones...");

            int n = Zones.Count;

            // Pre-compute zone bounds in device coordinates (int for faster comparison)
            _zoneX1 = new int[n];
            _zoneY1 = new int[n];
            _zoneX2 = new int[n];
            _zoneY2 = new int[n];
            _zoneVk = new ushort[n];
            _zoneScan = new ushort[n];
            _activeZones = new bool[n];
            _activeZoneIndices = new int[n];
            _activeZoneCount = 0;

            // Initialize contact tracking
            _trackId = new int[MAX_TRACKED_CONTACTS];
            _trackZone = new int[MAX_TRACKED_CONTACTS];
            _trackSeen = new bool[MAX_TRACKED_CONTACTS];
            _trackCount = 0;

            // Cache INPUT struct size once
            _inputStructSize = Marshal.SizeOf(typeof(INPUT));

            for (int i = 0; i < n; i++)
            {
                var z = Zones[i];
                _zoneX1[i] = TouchpadDevice.X_Lo + (int)z.Region.Position.X;
                _zoneY1[i] = TouchpadDevice.Y_Lo + (int)z.Region.Position.Y;
                _zoneX2[i] = _zoneX1[i] + (int)z.Region.Width;
                _zoneY2[i] = _zoneY1[i] + (int)z.Region.Height;
                _zoneVk[i] = KeyNameToVk(z.Key);
                _zoneScan[i] = (ushort)MapVirtualKey(_zoneVk[i], MAPVK_VK_TO_VSC);
                _activeZones[i] = false;
                FireOutput("Zone " + (i + 1) + ": [" + z.Key + " = 0x" + _zoneVk[i].ToString("X2") + "] "
                    + (int)z.Region.Position.X + "," + (int)z.Region.Position.Y + " " + (int)z.Region.Width + "x" + (int)z.Region.Height);
            }

            _firstTouch = true;

            // Initialize performance tracking
            if (PerfEnabled)
            {
                if (_handlerWatch == null) _handlerWatch = new Stopwatch();
                _handlerWatch.Restart();
                _totalHandlerTicks = 0;
                _minHandlerTicks = long.MaxValue;
                _maxHandlerTicks = 0;
                _handlerCount = 0;
                _lastStatTicks = 0;
                API.EnablePerformanceTracking(true);
            }

            // Start dedicated high-priority input thread
            // This thread creates a hidden window, registers Raw Input, installs hooks,
            // and runs its own message pump — completely decoupled from WPF UI thread.
            // Hooks suppress all gesture side-effects (cursor, injected shortcuts).
            StartInputThread();

            if (!_inputStartedOK)
            {
                FireOutput("Failed to start input thread.");
                return;
            }

            IsActive = true;
            FireOutput("Mania mode active.");
        }

        public void Stop()
        {
            // Signal input thread to stop — it handles all cleanup:
            // unhook, unregister raw input, release held keys, destroy window
            StopInputThread();

            // Clear visual feedback
            var zoneHandler = ActiveZonesChanged;
            if (zoneHandler != null) zoneHandler(_emptyZoneIndices);

            IsActive = false;
            FireOutput("Mania mode stopped.");
        }

        private bool _firstTouch = true;

        // Contact tracking for incremental per-report processing (zero-alloc)
        private const int MAX_TRACKED_CONTACTS = 16;
        private int[] _trackId;        // contact IDs currently tracked
        private int[] _trackZone;      // zone index for each tracked contact (-1 = not in any zone)
        private bool[] _trackSeen;     // seen in current frame (for stale contact cleanup)
        private int _trackCount;

        private int FindTrackedContact(int id)
        {
            for (int i = 0; i < _trackCount; i++)
                if (_trackId[i] == id) return i;
            return -1;
        }

        private int FindZoneForPoint(int x, int y)
        {
            int zoneCount = _zoneVk.Length;
            for (int z = 0; z < zoneCount; z++)
            {
                if (x >= _zoneX1[z] && x <= _zoneX2[z] &&
                    y >= _zoneY1[z] && y <= _zoneY2[z])
                    return z;
            }
            return -1;
        }

        private bool AnyTrackedContactInZone(int excludeIndex, int zone)
        {
            for (int i = 0; i < _trackCount; i++)
                if (i != excludeIndex && _trackZone[i] == zone) return true;
            return false;
        }

        private void PressZone(int zone)
        {
            if (zone >= 0 && !_activeZones[zone])
            {
                _activeZones[zone] = true;
                SendKey(_zoneVk[zone], _zoneScan[zone], false);
            }
        }

        private void ReleaseZone(int zone)
        {
            if (zone >= 0 && _activeZones[zone])
            {
                _activeZones[zone] = false;
                SendKey(_zoneVk[zone], _zoneScan[zone], true);
            }
        }

        /// <summary>Fires immediately for each individual HID contact report — no frame-boundary wait.</summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void HandleContactUpdate(int id, int x, int y, bool isDown)
        {
            try
            {
                HandleContactUpdateCore(id, x, y, isDown);
            }
            catch (Exception ex)
            {
                App.WriteLog("CRASH HandleContactUpdate(id=" + id + " x=" + x + " y=" + y + " down=" + isDown + " trackCount=" + _trackCount + "): " + ex);
            }
        }

        private void HandleContactUpdateCore(int id, int x, int y, bool isDown)
        {
            long startTicks = 0;
            if (PerfEnabled && _handlerWatch != null)
                startTicks = _handlerWatch.ElapsedTicks;

            if (_firstTouch && isDown)
            {
                _firstTouch = false;
                FireOutput("First multi-touch event: 1 contacts");
                FireOutput("  Contact 0: id=" + id + " x=" + x + " y=" + y);
            }

            if (isDown)
            {
                int zone = FindZoneForPoint(x, y);
                int idx = FindTrackedContact(id);

                if (idx >= 0)
                {
                    // Known contact — mark seen
                    _trackSeen[idx] = true;

                    int prevZone = _trackZone[idx];
                    if (prevZone != zone)
                    {
                        // Contact moved to different zone
                        if (prevZone >= 0 && !AnyTrackedContactInZone(idx, prevZone))
                            ReleaseZone(prevZone);
                        _trackZone[idx] = zone;
                        PressZone(zone);
                    }
                }
                else if (_trackCount < MAX_TRACKED_CONTACTS)
                {
                    // New contact
                    idx = _trackCount++;
                    _trackId[idx] = id;
                    _trackZone[idx] = zone;
                    _trackSeen[idx] = true;
                    PressZone(zone);
                }
            }
            else
            {
                // Contact lifted — release zone immediately
                int idx = FindTrackedContact(id);
                if (idx >= 0)
                {
                    int prevZone = _trackZone[idx];

                    // Remove by swapping with last
                    _trackCount--;
                    if (idx < _trackCount)
                    {
                        _trackId[idx] = _trackId[_trackCount];
                        _trackZone[idx] = _trackZone[_trackCount];
                        _trackSeen[idx] = _trackSeen[_trackCount];
                    }

                    // Release zone if no other contact in it
                    if (prevZone >= 0 && !AnyTrackedContactInZone(-1, prevZone))
                        ReleaseZone(prevZone);
                }
            }

            // Performance tracking
            if (PerfEnabled && _handlerWatch != null)
            {
                long endTicks = _handlerWatch.ElapsedTicks;
                long latency = endTicks - startTicks;
                _handlerCount++;
                _totalHandlerTicks += latency;
                if (latency < _minHandlerTicks) _minHandlerTicks = latency;
                if (latency > _maxHandlerTicks) _maxHandlerTicks = latency;

                // Output stats every second
                if (endTicks - _lastStatTicks >= Stopwatch.Frequency)
                {
                    double avgUs = (_totalHandlerTicks * 1000000.0) / (_handlerCount * Stopwatch.Frequency);
                    double minUs = (_minHandlerTicks * 1000000.0) / Stopwatch.Frequency;
                    double maxUs = (_maxHandlerTicks * 1000000.0) / Stopwatch.Frequency;

                    FireOutput(string.Format("[MANIA] Total: avg={0:F0}μs min={1:F0}μs max={2:F0}μs | {3} events",
                        avgUs, minUs, maxUs, _handlerCount));

                    _lastStatTicks = endTicks;
                    _totalHandlerTicks = 0;
                    _minHandlerTicks = long.MaxValue;
                    _maxHandlerTicks = 0;
                    _handlerCount = 0;
                }
            }
        }

        /// <summary>Called at frame boundary — clean up stale contacts and fire visual feedback.</summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void HandleFrameComplete()
        {
            try
            {
                HandleFrameCompleteCore();
            }
            catch (Exception ex)
            {
                App.WriteLog("CRASH HandleFrameComplete(trackCount=" + _trackCount + "): " + ex);
            }
        }

        private void HandleFrameCompleteCore()
        {
            // Check for stale contacts (not reported in this frame — e.g. contactCount decreased)
            for (int i = _trackCount - 1; i >= 0; i--)
            {
                if (!_trackSeen[i])
                {
                    int prevZone = _trackZone[i];
                    // Remove by swapping with last
                    _trackCount--;
                    if (i < _trackCount)
                    {
                        _trackId[i] = _trackId[_trackCount];
                        _trackZone[i] = _trackZone[_trackCount];
                        _trackSeen[i] = _trackSeen[_trackCount];
                    }
                    if (prevZone >= 0 && !AnyTrackedContactInZone(-1, prevZone))
                        ReleaseZone(prevZone);
                }
            }

            // Clear seen flags for next frame
            for (int i = 0; i < _trackCount; i++)
                _trackSeen[i] = false;

            // Fire visual feedback
            if (VisualFeedbackEnabled)
            {
                var zoneHandler = ActiveZonesChanged;
                if (zoneHandler != null)
                {
                    int activeCount = 0;
                    for (int z = 0; z < _activeZones.Length; z++)
                        if (_activeZones[z])
                            _activeZoneIndices[activeCount++] = z;

                    zoneHandler(activeCount == 0 ? _emptyZoneIndices : _activeZoneIndices);
                }
            }
        }

        /// <summary>Called when contactCount=0 and previous frame had contacts — all fingers lifted.</summary>
        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void HandleAllContactsLifted()
        {
            try
            {
                HandleAllContactsLiftedCore();
            }
            catch (Exception ex)
            {
                App.WriteLog("CRASH HandleAllContactsLifted: " + ex);
            }
        }

        private void HandleAllContactsLiftedCore()
        {
            int zoneCount = _zoneVk.Length;
            for (int z = 0; z < zoneCount; z++)
                ReleaseZone(z);
            _trackCount = 0;

            if (VisualFeedbackEnabled)
            {
                var zoneHandler = ActiveZonesChanged;
                if (zoneHandler != null) zoneHandler(_emptyZoneIndices);
            }
        }

        private void SendKey(ushort vk, ushort scan, bool keyUp)
        {
            if (vk == 0) return;

            _inputBuffer[0].type = INPUT_KEYBOARD;
            _inputBuffer[0].u.ki.wVk = vk;
            _inputBuffer[0].u.ki.wScan = scan;
            _inputBuffer[0].u.ki.dwFlags = keyUp ? KEYEVENTF_KEYUP : 0;
            _inputBuffer[0].u.ki.dwExtraInfo = EXTRAINFO_MARKER;
            SendInput(1, _inputBuffer, _inputStructSize);
        }

        private void FireOutput(string msg)
        {
            var h = Output;
            if (h != null) h(this, msg);
        }

        #region Key Name Mapping

        public static ushort KeyNameToVk(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            name = name.Trim().ToUpperInvariant();

            // Single letter A-Z
            if (name.Length == 1 && name[0] >= 'A' && name[0] <= 'Z')
                return (ushort)(0x41 + (name[0] - 'A'));

            // Single digit 0-9
            if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
                return (ushort)(0x30 + (name[0] - '0'));

            // Function keys
            if (name.StartsWith("F") && name.Length >= 2)
            {
                int num;
                if (int.TryParse(name.Substring(1), out num) && num >= 1 && num <= 24)
                    return (ushort)(0x70 + num - 1);
            }

            switch (name)
            {
                case "SPACE": return 0x20;
                case "ENTER": case "RETURN": return 0x0D;
                case "SHIFT": case "LSHIFT": return 0xA0;
                case "RSHIFT": return 0xA1;
                case "CTRL": case "LCTRL": case "CONTROL": return 0xA2;
                case "RCTRL": return 0xA3;
                case "ALT": case "LALT": return 0xA4;
                case "RALT": return 0xA5;
                case "TAB": return 0x09;
                case "ESC": case "ESCAPE": return 0x1B;
                case "UP": return 0x26;
                case "DOWN": return 0x28;
                case "LEFT": return 0x25;
                case "RIGHT": return 0x27;
                case "COMMA": return 0xBC;
                case "PERIOD": return 0xBE;
                case "SEMICOLON": return 0xBA;
                case "SLASH": return 0xBF;
                case "BACKSLASH": return 0xDC;
                case "MINUS": return 0xBD;
                case "EQUALS": return 0xBB;
                case "LBRACKET": return 0xDB;
                case "RBRACKET": return 0xDD;
                case "QUOTE": return 0xDE;
                case "BACKQUOTE": case "TILDE": return 0xC0;
            }

            // Single character — use VkKeyScan for symbols like , . ; / etc.
            if (name.Length == 1)
            {
                short vks = VkKeyScan(name[0]);
                if (vks != -1)
                    return (ushort)(vks & 0xFF);
            }

            return 0;
        }

        public static readonly string[] CommonKeys = new string[]
        {
            "Z", "X", "C", "V",      // osu!mania defaults
            "A", "S", "D", "F",      // alternative
            "Q", "W", "E", "R",
            "SPACE", "SHIFT", "CTRL",
        };

        #endregion
    }
}
