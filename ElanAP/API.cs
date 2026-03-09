using System;
using System.Runtime.InteropServices;
using System.Security;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace ElanAP
{
    public class API
    {
        #region Win32 Raw Input

        [DllImport("user32.dll", SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true), SuppressUnmanagedCodeSecurity]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIM_TYPEHID = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        #endregion

        public event EventHandler<string> Output;

        public event Action<int, int> OnTouch;
        public event Action OnLift;

        public bool IsAvailable { get; private set; }

        public int X_Lo { get; private set; }
        public int X_Hi { get; private set; }
        public int Y_Lo { get; private set; }
        public int Y_Hi { get; private set; }

        private HidDevice _device;
        private DeviceItem _touchpadItem;
        private ReportDescriptor _descriptor;
        private DeviceItemInputParser _parser;
        private volatile bool _running;

        // Pre-allocated buffers to avoid GC pressure on hot path
        private readonly uint _headerSize;
        private byte[] _rawBuffer;
        private byte[] _reportBuffer;

        // Cached value indices (discovered on first parse, avoids GetAllValues iteration)
        private bool _indicesCached;
        private int _tipSwitchIdx = -1;
        private int _xIdx = -1;
        private int _yIdx = -1;

        public API()
        {
            _headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            _rawBuffer = new byte[512];
            _reportBuffer = new byte[64];
            X_Lo = 0; X_Hi = 3200; Y_Lo = 0; Y_Hi = 2000;
            try
            {
                Init();
                IsAvailable = true;
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                FireOutput("API init failed: " + ex.Message);
            }
        }

        private void FireOutput(string msg)
        {
            var h = Output;
            if (h != null) h(this, msg);
        }

        private void Init()
        {
            foreach (var d in DeviceList.Local.GetHidDevices())
            {
                try
                {
                    var desc = d.GetReportDescriptor();
                    foreach (var item in desc.DeviceItems)
                    {
                        foreach (uint enc in item.Usages.GetAllValues())
                        {
                            if (enc == 0x000D0005u)
                            {
                                _device = d;
                                _touchpadItem = item;
                                _descriptor = desc;
                                _parser = item.CreateDeviceItemInputParser();
                                ExtractRanges(item);
                                FireOutput("API initialized. Device: " + d.GetFriendlyName()
                                    + "  X:[" + X_Lo + "," + X_Hi + "]"
                                    + "  Y:[" + Y_Lo + "," + Y_Hi + "]");
                                return;
                            }
                        }
                    }
                }
                catch { }
            }
            throw new Exception("No HID Precision Touchpad found. Ensure Elan driver is installed.");
        }

        private void ExtractRanges(DeviceItem item)
        {
            foreach (var report in item.InputReports)
            {
                foreach (var di in report.DataItems)
                {
                    foreach (uint enc in di.Usages.GetAllValues())
                    {
                        if (enc == 0x00010030u)
                        {
                            X_Lo = (int)di.LogicalMinimum;
                            X_Hi = (int)di.LogicalMaximum;
                        }
                        else if (enc == 0x00010031u)
                        {
                            Y_Lo = (int)di.LogicalMinimum;
                            Y_Hi = (int)di.LogicalMaximum;
                        }
                    }
                }
            }
        }

        public bool StartReading(IntPtr hwnd)
        {
            if (_running) return true;

            var rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x000D;
            rid[0].usUsage = 0x05;
            rid[0].dwFlags = RIDEV_INPUTSINK;
            rid[0].hwndTarget = hwnd;

            if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            {
                FireOutput("Failed to register Raw Input. Error code: " + Marshal.GetLastWin32Error());
                return false;
            }

            _running = true;
            _indicesCached = false; // re-discover on next parse
            FireOutput("Raw Input registered. Reading started.");
            return true;
        }

        public void StopReading()
        {
            _running = false;
            var rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x000D;
            rid[0].usUsage = 0x05;
            rid[0].dwFlags = RIDEV_REMOVE;
            rid[0].hwndTarget = IntPtr.Zero;
            RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            FireOutput("Reading stopped.");
        }

        public void ProcessRawInput(IntPtr lParam)
        {
            if (!_running) return;

            uint dwSize = 0;
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, _headerSize);
            if (dwSize == 0) return;

            // Reuse buffer — grow only if needed
            if (_rawBuffer.Length < (int)dwSize)
                _rawBuffer = new byte[(int)dwSize];

            IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
            try
            {
                if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, _headerSize) != dwSize)
                    return;

                // Read dwType directly from unmanaged memory (offset 0 of RAWINPUTHEADER)
                uint dwType = (uint)Marshal.ReadInt32(buffer, 0);
                if (dwType != RIM_TYPEHID) return;

                // Copy only the HID payload portion, not the full buffer
                int hidOffset = (int)_headerSize;
                int payloadLen = (int)dwSize - hidOffset;
                if (payloadLen < 8) return;

                Marshal.Copy(buffer + hidOffset, _rawBuffer, 0, payloadLen);

                int dwSizeHid = BitConverter.ToInt32(_rawBuffer, 0);
                int dwCount = BitConverter.ToInt32(_rawBuffer, 4);
                if (dwSizeHid <= 0 || dwCount <= 0) return;

                // Reuse report buffer — grow only if needed
                if (_reportBuffer.Length < dwSizeHid)
                    _reportBuffer = new byte[dwSizeHid];

                for (int r = 0; r < dwCount; r++)
                {
                    int offset = 8 + r * dwSizeHid;
                    if (offset + dwSizeHid > payloadLen) break;
                    Array.Copy(_rawBuffer, offset, _reportBuffer, 0, dwSizeHid);
                    ParseReport(_reportBuffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void ParseReport(byte[] reportBytes)
        {
            byte reportId = _descriptor.ReportsUseID ? reportBytes[0] : (byte)0;
            Report report;
            if (!_descriptor.TryGetReport(ReportType.Input, reportId, out report))
                return;

            if (!_parser.TryParseReport(reportBytes, 0, report))
                return;

            // Fast path: use cached indices to avoid iterating all usages every frame
            if (_indicesCached)
            {
                bool fingerDown = false;
                int x = 0, y = 0;

                if (_tipSwitchIdx >= 0)
                    fingerDown = _parser.GetValue(_tipSwitchIdx).GetLogicalValue() != 0;
                if (_xIdx >= 0)
                    x = _parser.GetValue(_xIdx).GetLogicalValue();
                if (_yIdx >= 0)
                    y = _parser.GetValue(_yIdx).GetLogicalValue();

                if (fingerDown)
                {
                    var touchHandler = OnTouch;
                    if (touchHandler != null) touchHandler(x, y);
                }
                else
                {
                    var liftHandler = OnLift;
                    if (liftHandler != null) liftHandler();
                }
                return;
            }

            // First parse: discover and cache indices
            bool fd = false;
            int fx = 0, fy = 0;
            bool gotFirstXY = false;

            for (int i = 0; i < _parser.ValueCount; i++)
            {
                var dv = _parser.GetValue(i);
                int logVal = dv.GetLogicalValue();

                foreach (uint enc in dv.DataItem.Usages.GetAllValues())
                {
                    if (enc == 0x000D0042u)
                    {
                        fd |= (logVal != 0);
                        if (_tipSwitchIdx < 0) _tipSwitchIdx = i;
                    }
                    else if (enc == 0x00010030u && !gotFirstXY)
                    {
                        fx = logVal;
                        if (_xIdx < 0) _xIdx = i;
                    }
                    else if (enc == 0x00010031u && !gotFirstXY)
                    {
                        fy = logVal;
                        gotFirstXY = true;
                        if (_yIdx < 0) _yIdx = i;
                    }
                }
            }

            _indicesCached = (_tipSwitchIdx >= 0 && _xIdx >= 0 && _yIdx >= 0);
            if (_indicesCached)
                FireOutput("Parser indices cached: tip=" + _tipSwitchIdx + " x=" + _xIdx + " y=" + _yIdx);

            if (fd)
            {
                var touchHandler = OnTouch;
                if (touchHandler != null) touchHandler(fx, fy);
            }
            else
            {
                var liftHandler = OnLift;
                if (liftHandler != null) liftHandler();
            }
        }
    }
}
