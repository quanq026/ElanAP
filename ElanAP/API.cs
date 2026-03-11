using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        /// <summary>Multi-touch event: fires with an array of active contacts (id, x, y) per report.</summary>
        public event Action<TouchContact[]> OnMultiTouch;

        /// <summary>Per-contact update: fires immediately for each contact in each HID report.</summary>
        public event Action<int, int, int, bool> OnContactUpdate;

        /// <summary>Fires when all contact slots in a frame have been reported.</summary>
        public event Action OnFrameComplete;

        /// <summary>Fires when contactCount=0 and previous frame had contacts (all fingers lifted).</summary>
        public event Action OnAllContactsLifted;

        public struct TouchContact
        {
            public int Id;
            public int X;
            public int Y;
        }

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

        private bool _indicesCached;
        private int _tipSwitchIdx = -1;
        private int _xIdx = -1;
        private int _yIdx = -1;

        private bool _multiIndicesCached;
        private int _contactCountIdx = -1;
        private int[] _mtTipIdx;   // tipSwitch per contact
        private int[] _mtXIdx;     // X per contact
        private int[] _mtYIdx;     // Y per contact
        private int[] _mtIdIdx;    // contactID per contact
        private int _maxContacts;

        private TouchContact[] _batchContacts;
        private int _batchCount;
        private int _expectedContactCount;
        private int _reportedContactSlots;
        private bool _frameStarted;
        private bool _hadContacts;

        // Direct byte parsing — bypasses HidSharp on hot path (zero-alloc)
        private struct DirectField
        {
            public int BitOffset;
            public int BitSize;
            public bool IsSigned;
        }
        private bool _directReady;
        private bool _useReportId;
        private byte _expectedReportId;
        private DirectField[] _directTip;
        private DirectField[] _directX;
        private DirectField[] _directY;
        private DirectField[] _directId;
        private DirectField _directContactCount;

        private IntPtr _unmanagedBuffer = IntPtr.Zero;
        private int _unmanagedBufferSize;

        // Performance tracking
        private Stopwatch _perfWatch;
        private long _frameCount;
        private long _lastStatTicks;
        private long _totalLatencyTicks;
        private long _minLatencyTicks;
        private long _maxLatencyTicks;
        private int _statFrameCount;
        private bool _perfEnabled;

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

        /// <summary>Enable/disable performance monitoring output.</summary>
        public void EnablePerformanceTracking(bool enable)
        {
            _perfEnabled = enable;
            if (enable)
                FireOutput("[PERF] Performance tracking ENABLED");
        }

        private void FireOutput(string msg)
        {
            var h = Output;
            if (h != null) h(this, msg);
        }

        private void Init()
        {
            FireOutput("Scanning HID devices...");
            int deviceIndex = 0;
            foreach (var d in DeviceList.Local.GetHidDevices())
            {
                try
                {
                    string name = "";
                    try { name = d.GetFriendlyName(); } catch { name = "(no name)"; }
                    var desc = d.GetReportDescriptor();
                    foreach (var item in desc.DeviceItems)
                    {
                        var usages = new List<string>();
                        foreach (uint enc in item.Usages.GetAllValues())
                            usages.Add("0x" + enc.ToString("X8"));

                        FireOutput("  [" + deviceIndex + "] " + name
                            + " VID:0x" + d.VendorID.ToString("X4")
                            + " PID:0x" + d.ProductID.ToString("X4")
                            + " Usages: " + string.Join(", ", usages));

                        foreach (uint enc in item.Usages.GetAllValues())
                        {
                            if (enc == 0x000D0005u) // Precision Touchpad
                            {
                                _device = d;
                                _touchpadItem = item;
                                _descriptor = desc;
                                _parser = item.CreateDeviceItemInputParser();
                                ExtractRanges(item);
                                FireOutput("=> Selected as touchpad. X:[" + X_Lo + "," + X_Hi + "] Y:[" + Y_Lo + "," + Y_Hi + "]");
                                return;
                            }
                        }
                    }
                }
                catch { }
                deviceIndex++;
            }
            throw new Exception("No HID Precision Touchpad found (usage 0x000D0005). Check console log for detected devices.");
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
            _multiIndicesCached = false;
            _directReady = false;
            _batchCount = 0;
            _expectedContactCount = 0;
            _reportedContactSlots = 0;
            _frameStarted = false;
            _hadContacts = false;

            // Initialize performance tracking
            if (_perfWatch == null) _perfWatch = new Stopwatch();
            _perfWatch.Restart();
            _frameCount = 0;
            _lastStatTicks = 0;
            _totalLatencyTicks = 0;
            _minLatencyTicks = long.MaxValue;
            _maxLatencyTicks = 0;
            _statFrameCount = 0;

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

            if (_unmanagedBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_unmanagedBuffer);
                _unmanagedBuffer = IntPtr.Zero;
                _unmanagedBufferSize = 0;
            }

            FireOutput("Reading stopped.");
        }

        public void ProcessRawInput(IntPtr lParam)
        {
            if (!_running) return;

            long startTicks = 0;
            if (_perfEnabled && _perfWatch != null)
                startTicks = _perfWatch.ElapsedTicks;

            uint dwSize = 0;
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, _headerSize);
            if (dwSize == 0) return;

            if (_unmanagedBuffer == IntPtr.Zero || _unmanagedBufferSize < (int)dwSize)
            {
                if (_unmanagedBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_unmanagedBuffer);
                _unmanagedBufferSize = (int)dwSize + 128;
                _unmanagedBuffer = Marshal.AllocHGlobal(_unmanagedBufferSize);
            }

            if (GetRawInputData(lParam, RID_INPUT, _unmanagedBuffer, ref dwSize, _headerSize) != dwSize)
                return;

            uint dwType = (uint)Marshal.ReadInt32(_unmanagedBuffer, 0);
            if (dwType != RIM_TYPEHID) return;

            int hidOffset = (int)_headerSize;
            int payloadLen = (int)dwSize - hidOffset;
            if (payloadLen < 8) return;

            if (_rawBuffer.Length < payloadLen)
                _rawBuffer = new byte[payloadLen + 64];

            Marshal.Copy(_unmanagedBuffer + hidOffset, _rawBuffer, 0, payloadLen);

            int dwSizeHid = BitConverter.ToInt32(_rawBuffer, 0);
            int dwCount = BitConverter.ToInt32(_rawBuffer, 4);
            if (dwSizeHid <= 0 || dwCount <= 0) return;

            if (_reportBuffer.Length < dwSizeHid)
                _reportBuffer = new byte[dwSizeHid];

            bool maniaMode = OnMultiTouch != null || OnContactUpdate != null;

            for (int r = 0; r < dwCount; r++)
            {
                int offset = 8 + r * dwSizeHid;
                if (offset + dwSizeHid > payloadLen) break;
                Array.Copy(_rawBuffer, offset, _reportBuffer, 0, dwSizeHid);
                ParseReport(_reportBuffer, maniaMode);
            }

            if (maniaMode && _frameStarted && _reportedContactSlots >= _expectedContactCount)
            {
                var fch = OnFrameComplete;
                if (fch != null) fch();
                FireMultiTouch();
            }

            if (_perfEnabled && _perfWatch != null)
            {
                long endTicks = _perfWatch.ElapsedTicks;
                long latency = endTicks - startTicks;
                _frameCount++;
                _statFrameCount++;
                _totalLatencyTicks += latency;
                if (latency < _minLatencyTicks) _minLatencyTicks = latency;
                if (latency > _maxLatencyTicks) _maxLatencyTicks = latency;

                if (endTicks - _lastStatTicks >= Stopwatch.Frequency)
                {
                    double avgLatencyUs = (_totalLatencyTicks * 1000000.0) / (_statFrameCount * Stopwatch.Frequency);
                    double minLatencyUs = (_minLatencyTicks * 1000000.0) / Stopwatch.Frequency;
                    double maxLatencyUs = (_maxLatencyTicks * 1000000.0) / Stopwatch.Frequency;
                    double hz = _statFrameCount;

                    FireOutput(string.Format("[PERF] {0:F0}Hz | Lat: avg={1:F0}μs min={2:F0}μs max={3:F0}μs | Total: {4}",
                        hz, avgLatencyUs, minLatencyUs, maxLatencyUs, _frameCount));

                    _lastStatTicks = endTicks;
                    _totalLatencyTicks = 0;
                    _minLatencyTicks = long.MaxValue;
                    _maxLatencyTicks = 0;
                    _statFrameCount = 0;
                }
            }
        }

        private static readonly TouchContact[] _emptyContacts = new TouchContact[0];
        private TouchContact[] _snapshotContacts;

        private void FireMultiTouch()
        {
            var batchHandler = OnMultiTouch;
            if (batchHandler != null)
            {
                if (_batchCount == 0)
                {
                    batchHandler(_emptyContacts);
                }
                else
                {
                    if (_snapshotContacts == null || _snapshotContacts.Length != _batchCount)
                        _snapshotContacts = new TouchContact[_batchCount];
                    Array.Copy(_batchContacts, _snapshotContacts, _batchCount);
                    batchHandler(_snapshotContacts);
                }
            }
            _hadContacts = (_batchCount > 0);
            _batchCount = 0;
            _reportedContactSlots = 0;
            _frameStarted = false;
        }

        private void ParseReport(byte[] reportBytes, bool maniaMode)
        {
            if (maniaMode && _directReady)
            {
                if (_useReportId && reportBytes[0] != _expectedReportId)
                    return;
                CollectMultiTouchDirect(reportBytes);
                return;
            }

            byte reportId = _descriptor.ReportsUseID ? reportBytes[0] : (byte)0;
            Report report;
            if (!_descriptor.TryGetReport(ReportType.Input, reportId, out report))
                return;

            if (!_parser.TryParseReport(reportBytes, 0, report))
                return;

            if (!maniaMode && _indicesCached)
            {
                bool fingerDown = _parser.GetValue(_tipSwitchIdx).GetLogicalValue() != 0;
                if (fingerDown)
                {
                    var touchHandler = OnTouch;
                    if (touchHandler != null)
                        touchHandler(
                            _parser.GetValue(_xIdx).GetLogicalValue(),
                            _parser.GetValue(_yIdx).GetLogicalValue());
                }
                else
                {
                    var liftHandler = OnLift;
                    if (liftHandler != null) liftHandler();
                }
            }

            if (maniaMode)
            {
                if (_multiIndicesCached)
                {
                    CollectMultiTouchIntoBatch();
                }
                else
                {
                    DiscoverMultiTouchIndices(reportBytes, report);
                }
            }
            else if (!_indicesCached)
            {
                DiscoverSingleTouchIndices(reportBytes, report);
            }
        }

        private void CollectMultiTouchIntoBatch()
        {
            int reportContactCount = 0;
            if (_contactCountIdx >= 0)
                reportContactCount = _parser.GetValue(_contactCountIdx).GetLogicalValue();

            if (reportContactCount > 0)
            {
                if (_frameStarted && _batchCount > 0)
                    FireMultiTouch();

                _batchCount = 0;
                _reportedContactSlots = 0;
                _expectedContactCount = reportContactCount;
                _frameStarted = true;
            }
            else if (_contactCountIdx >= 0 && !_frameStarted && _hadContacts)
            {
                _batchCount = 0;
                _hadContacts = false;
                var alh = OnAllContactsLifted;
                if (alh != null) alh();
                FireMultiTouch();
                return;
            }

            if (!_frameStarted) return;

            // Each HID report = 1 contact in hybrid mode
            bool foundContact = false;
            for (int c = 0; c < _maxContacts; c++)
            {
                bool down = _mtTipIdx[c] >= 0 && _parser.GetValue(_mtTipIdx[c]).GetLogicalValue() != 0;
                if (down)
                {
                    int cx = _mtXIdx[c] >= 0 ? _parser.GetValue(_mtXIdx[c]).GetLogicalValue() : 0;
                    int cy = _mtYIdx[c] >= 0 ? _parser.GetValue(_mtYIdx[c]).GetLogicalValue() : 0;
                    int cid = _mtIdIdx[c] >= 0 ? _parser.GetValue(_mtIdIdx[c]).GetLogicalValue() : c;

                    if (_batchContacts == null || _batchCount >= _batchContacts.Length)
                    {
                        var newArr = new TouchContact[(_batchContacts == null ? 0 : _batchContacts.Length) + 8];
                        if (_batchContacts != null) Array.Copy(_batchContacts, newArr, _batchCount);
                        _batchContacts = newArr;
                    }
                    _batchContacts[_batchCount].X = cx;
                    _batchContacts[_batchCount].Y = cy;
                    _batchContacts[_batchCount].Id = cid;
                    _batchCount++;

                    // Fire immediate per-contact event (before frame completes)
                    var cuh = OnContactUpdate;
                    if (cuh != null) cuh(cid, cx, cy, true);

                    foundContact = true;
                }
                else if (_mtTipIdx[c] >= 0)
                {
                    int cid = _mtIdIdx[c] >= 0 ? _parser.GetValue(_mtIdIdx[c]).GetLogicalValue() : c;
                    var cuh = OnContactUpdate;
                    if (cuh != null) cuh(cid, 0, 0, false);

                    foundContact = true;
                }
            }

            if (foundContact)
                _reportedContactSlots++;
        }

        private void DiscoverMultiTouchIndices(byte[] reportBytes, Report report)
        {
            var tipList = new System.Collections.Generic.List<int>();
            var xList = new System.Collections.Generic.List<int>();
            var yList = new System.Collections.Generic.List<int>();
            var idList = new System.Collections.Generic.List<int>();
            int contactCountIdx = -1;

            bool firstTip = true, firstX = true, firstY = true;

            for (int i = 0; i < _parser.ValueCount; i++)
            {
                var dv = _parser.GetValue(i);
                foreach (uint enc in dv.DataItem.Usages.GetAllValues())
                {
                    if (enc == 0x000D0042u)  // Tip Switch
                    {
                        tipList.Add(i);
                        if (firstTip) { _tipSwitchIdx = i; firstTip = false; }
                    }
                    else if (enc == 0x00010030u)  // X
                    {
                        xList.Add(i);
                        if (firstX) { _xIdx = i; firstX = false; }
                    }
                    else if (enc == 0x00010031u)  // Y
                    {
                        yList.Add(i);
                        if (firstY) { _yIdx = i; firstY = false; }
                    }
                    else if (enc == 0x000D0051u)  // Contact ID
                    {
                        idList.Add(i);
                    }
                    else if (enc == 0x000D0054u)  // Contact Count
                    {
                        contactCountIdx = i;
                    }
                }
            }

            _indicesCached = (_tipSwitchIdx >= 0 && _xIdx >= 0 && _yIdx >= 0);

            int n = tipList.Count;
            if (n > 0)
            {
                _maxContacts = n;
                _contactCountIdx = contactCountIdx;
                _mtTipIdx = tipList.ToArray();
                _mtXIdx = new int[n];
                _mtYIdx = new int[n];
                _mtIdIdx = new int[n];
                for (int c = 0; c < n; c++)
                {
                    _mtXIdx[c] = c < xList.Count ? xList[c] : -1;
                    _mtYIdx[c] = c < yList.Count ? yList[c] : -1;
                    _mtIdIdx[c] = c < idList.Count ? idList[c] : -1;
                }
                _multiIndicesCached = true;
                FireOutput("Multi-touch indices cached: " + n + " contact slots"
                    + (contactCountIdx >= 0 ? ", contactCount at " + contactCountIdx : ""));

                // Build direct byte reading fields
                BuildDirectFields(reportBytes, report);

                CollectMultiTouchIntoBatch();
            }

            if (_indicesCached)
            {
                FireOutput("Parser indices cached: tip=" + _tipSwitchIdx + " x=" + _xIdx + " y=" + _yIdx);

                // Fire first single-touch event
                bool fd = _parser.GetValue(_tipSwitchIdx).GetLogicalValue() != 0;
                if (fd)
                {
                    var touchHandler = OnTouch;
                    if (touchHandler != null)
                        touchHandler(
                            _parser.GetValue(_xIdx).GetLogicalValue(),
                            _parser.GetValue(_yIdx).GetLogicalValue());
                }
                else
                {
                    var liftHandler = OnLift;
                    if (liftHandler != null) liftHandler();
                }
            }
        }

        #region Direct Byte Parsing (HidSharp bypass)

        private void BuildDirectFields(byte[] reportBytes, Report report)
        {
            try
            {
                var bitOffsets = new Dictionary<DataItem, int>();
                report.Read(reportBytes, 0, (buffer, bitOffset, dataItem, indexOfDataItem) =>
                {
                    if (!bitOffsets.ContainsKey(dataItem))
                        bitOffsets[dataItem] = bitOffset;
                });

                int n = _maxContacts;
                _directTip = new DirectField[n];
                _directX = new DirectField[n];
                _directY = new DirectField[n];
                _directId = new DirectField[n];
                _directContactCount = new DirectField();

                for (int c = 0; c < n; c++)
                {
                    if (_mtTipIdx[c] >= 0) _directTip[c] = MakeDirectField(_mtTipIdx[c], bitOffsets);
                    if (_mtXIdx[c] >= 0) _directX[c] = MakeDirectField(_mtXIdx[c], bitOffsets);
                    if (_mtYIdx[c] >= 0) _directY[c] = MakeDirectField(_mtYIdx[c], bitOffsets);
                    if (_mtIdIdx[c] >= 0) _directId[c] = MakeDirectField(_mtIdIdx[c], bitOffsets);
                }
                if (_contactCountIdx >= 0)
                    _directContactCount = MakeDirectField(_contactCountIdx, bitOffsets);

                _useReportId = _descriptor.ReportsUseID;
                _expectedReportId = _useReportId ? reportBytes[0] : (byte)0;
                _directReady = true;

                FireOutput("Direct byte parsing enabled: " + n + " contacts, bypassing HidSharp on hot path");
            }
            catch (Exception ex)
            {
                FireOutput("Warning: Could not build direct fields, using HidSharp fallback: " + ex.Message);
            }
        }

        private DirectField MakeDirectField(int parserIdx, Dictionary<DataItem, int> bitOffsets)
        {
            var dv = _parser.GetValue(parserIdx);
            var di = dv.DataItem;
            int baseBitOffset;
            if (bitOffsets.TryGetValue(di, out baseBitOffset))
            {
                return new DirectField
                {
                    BitOffset = baseBitOffset + dv.DataIndex * di.ElementBits,
                    BitSize = di.ElementBits,
                    IsSigned = di.IsLogicalSigned
                };
            }
            return new DirectField();
        }

        private static int ReadBitField(byte[] buf, int bitOff, int bits, bool signed)
        {
            int byteOff = bitOff >> 3;
            int shift = bitOff & 7;
            int need = (shift + bits + 7) >> 3;

            uint raw = 0;
            for (int i = 0; i < need; i++)
                raw |= (uint)buf[byteOff + i] << (i * 8);

            raw = (raw >> shift) & (uint)((1L << bits) - 1);

            if (signed && bits > 1 && (raw & (1u << (bits - 1))) != 0)
                return (int)(raw | (~0u << bits));

            return (int)raw;
        }

        private void CollectMultiTouchDirect(byte[] reportBytes)
        {
            int reportContactCount = 0;
            if (_directContactCount.BitSize > 0)
                reportContactCount = ReadBitField(reportBytes, _directContactCount.BitOffset, _directContactCount.BitSize, false);

            if (reportContactCount > 0)
            {
                if (_frameStarted && _batchCount > 0)
                    FireMultiTouch();
                _batchCount = 0;
                _reportedContactSlots = 0;
                _expectedContactCount = reportContactCount;
                _frameStarted = true;
            }
            else if (_directContactCount.BitSize > 0 && !_frameStarted && _hadContacts)
            {
                _batchCount = 0;
                _hadContacts = false;
                var alh = OnAllContactsLifted;
                if (alh != null) alh();
                FireMultiTouch();
                return;
            }

            if (!_frameStarted) return;

            bool foundContact = false;
            for (int c = 0; c < _maxContacts; c++)
            {
                bool down = _directTip[c].BitSize > 0 && ReadBitField(reportBytes, _directTip[c].BitOffset, _directTip[c].BitSize, false) != 0;
                if (down)
                {
                    int cx = _directX[c].BitSize > 0 ? ReadBitField(reportBytes, _directX[c].BitOffset, _directX[c].BitSize, _directX[c].IsSigned) : 0;
                    int cy = _directY[c].BitSize > 0 ? ReadBitField(reportBytes, _directY[c].BitOffset, _directY[c].BitSize, _directY[c].IsSigned) : 0;
                    int cid = _directId[c].BitSize > 0 ? ReadBitField(reportBytes, _directId[c].BitOffset, _directId[c].BitSize, false) : c;

                    if (_batchContacts == null || _batchCount >= _batchContacts.Length)
                    {
                        var newArr = new TouchContact[(_batchContacts == null ? 0 : _batchContacts.Length) + 8];
                        if (_batchContacts != null) Array.Copy(_batchContacts, newArr, _batchCount);
                        _batchContacts = newArr;
                    }
                    _batchContacts[_batchCount].X = cx;
                    _batchContacts[_batchCount].Y = cy;
                    _batchContacts[_batchCount].Id = cid;
                    _batchCount++;

                    var cuh = OnContactUpdate;
                    if (cuh != null) cuh(cid, cx, cy, true);
                    foundContact = true;
                }
                else if (_directTip[c].BitSize > 0)
                {
                    int cid = _directId[c].BitSize > 0 ? ReadBitField(reportBytes, _directId[c].BitOffset, _directId[c].BitSize, false) : c;
                    var cuh = OnContactUpdate;
                    if (cuh != null) cuh(cid, 0, 0, false);
                    foundContact = true;
                }
            }

            if (foundContact)
                _reportedContactSlots++;
        }

        #endregion

        private void DiscoverSingleTouchIndices(byte[] reportBytes, Report report)
        {
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
