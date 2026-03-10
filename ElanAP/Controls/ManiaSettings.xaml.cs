using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ElanAP.Controls
{
    public partial class ManiaSettings : UserControl
    {
        public ManiaSettings()
        {
            InitializeComponent();
        }

        public event EventHandler<string> Output;
        public event Action StartRequested;
        public event Action StopRequested;

        private Area _touchpadBounds;
        private int _keyCount = 4;
        private bool _vertical = true; // true = vertical columns (mania-style)
        private List<Zone> _zones = new List<Zone>();
        private string[] _defaultKeys = { "Z", "X", "C", "V", "A", "S", "D" };
        private bool _suppressEvents;

        // Zone area on touchpad (subset of full touchpad, like "bottom half")
        private Area _zoneRegion;

        // Zone visual tracking
        private Rectangle[] _zoneRects;

        private static readonly Brush[] ZoneColors = new Brush[]
        {
            new SolidColorBrush(Color.FromArgb(120, 66, 133, 244)),   // blue
            new SolidColorBrush(Color.FromArgb(120, 234, 67, 53)),    // red
            new SolidColorBrush(Color.FromArgb(120, 52, 168, 83)),    // green
            new SolidColorBrush(Color.FromArgb(120, 251, 188, 4)),    // yellow
            new SolidColorBrush(Color.FromArgb(120, 171, 71, 188)),   // purple
            new SolidColorBrush(Color.FromArgb(120, 255, 112, 67)),   // orange
            new SolidColorBrush(Color.FromArgb(120, 0, 172, 193)),    // teal
        };

        private static readonly Brush[] ZoneActiveColors = new Brush[]
        {
            new SolidColorBrush(Color.FromArgb(240, 66, 133, 244)),
            new SolidColorBrush(Color.FromArgb(240, 234, 67, 53)),
            new SolidColorBrush(Color.FromArgb(240, 52, 168, 83)),
            new SolidColorBrush(Color.FromArgb(240, 251, 188, 4)),
            new SolidColorBrush(Color.FromArgb(240, 171, 71, 188)),
            new SolidColorBrush(Color.FromArgb(240, 255, 112, 67)),
            new SolidColorBrush(Color.FromArgb(240, 0, 172, 193)),
        };

        public void Initialize(Area touchpadBounds)
        {
            _touchpadBounds = touchpadBounds;
            _zoneRegion = new Area(touchpadBounds.Width, touchpadBounds.Height,
                new Point(0, 0));
            RebuildZones();
        }

        public void LoadZones(List<Zone> zones)
        {
            if (zones == null || zones.Count == 0) return;
            _zones = zones;
            _keyCount = zones.Count;

            _suppressEvents = true;
            // Set combo to match
            for (int i = 0; i < KeyCountCombo.Items.Count; i++)
            {
                var item = (ComboBoxItem)KeyCountCombo.Items[i];
                if (item.Content.ToString() == _keyCount.ToString())
                {
                    KeyCountCombo.SelectedIndex = i;
                    break;
                }
            }
            PresetCombo.SelectedIndex = 0; // Full Area
            _suppressEvents = false;

            RebuildKeyBindPanel();
            DrawZones();
        }

        public List<Zone> GetZones()
        {
            return new List<Zone>(_zones);
        }

        private void RebuildZones()
        {
            _zones.Clear();

            double totalW = _zoneRegion.Width;
            double totalH = _zoneRegion.Height;
            double offX = _zoneRegion.Position.X;
            double offY = _zoneRegion.Position.Y;

            for (int i = 0; i < _keyCount; i++)
            {
                double zx, zy, zw, zh;
                if (_vertical)
                {
                    zw = totalW / _keyCount;
                    zh = totalH;
                    zx = offX + i * zw;
                    zy = offY;
                }
                else
                {
                    zw = totalW;
                    zh = totalH / _keyCount;
                    zx = offX;
                    zy = offY + i * zh;
                }

                string key = i < _defaultKeys.Length ? _defaultKeys[i] : ((char)('A' + i)).ToString();
                _zones.Add(new Zone(
                    new Area(Math.Round(zw), Math.Round(zh), new Point(Math.Round(zx), Math.Round(zy))),
                    key));
            }

            RebuildKeyBindPanel();
            DrawZones();
        }

        private void RebuildKeyBindPanel()
        {
            KeyBindPanel.Children.Clear();
            for (int i = 0; i < _zones.Count; i++)
            {
                var gb = new GroupBox
                {
                    Header = "Zone " + (i + 1),
                    Margin = new Thickness(4, 0, 4, 0),
                    Height = 50,
                };

                var tb = new TextBox
                {
                    Text = _zones[i].Key,
                    Width = 60,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Tag = i,
                };
                tb.TextChanged += KeyBindChanged;
                gb.Content = tb;
                KeyBindPanel.Children.Add(gb);
            }
        }

        private void KeyBindChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            int idx = (int)tb.Tag;
            if (idx >= 0 && idx < _zones.Count)
            {
                _zones[idx].Key = tb.Text.Trim().ToUpperInvariant();
                DrawZones();
            }
        }

        private void DrawZones()
        {
            ZoneCanvas.Children.Clear();
            _zoneRects = null;
            if (_touchpadBounds == null || _touchpadBounds.Width <= 0) return;

            double canvasW = ZoneCanvas.ActualWidth;
            double canvasH = ZoneCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return;

            double scaleX = canvasW / _touchpadBounds.Width;
            double scaleY = canvasH / _touchpadBounds.Height;
            double scale = Math.Min(scaleX, scaleY);

            double centerOffX = canvasW / 2.0 - _touchpadBounds.Width * scale / 2.0;
            double centerOffY = canvasH / 2.0 - _touchpadBounds.Height * scale / 2.0;

            // Draw background (full touchpad)
            var bg = new Rectangle
            {
                Width = _touchpadBounds.Width * scale,
                Height = _touchpadBounds.Height * scale,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
            };
            Canvas.SetLeft(bg, centerOffX);
            Canvas.SetTop(bg, centerOffY);
            ZoneCanvas.Children.Add(bg);

            _zoneRects = new Rectangle[_zones.Count];

            // Draw each zone
            for (int i = 0; i < _zones.Count; i++)
            {
                var z = _zones[i];
                double rx = centerOffX + z.Region.Position.X * scale;
                double ry = centerOffY + z.Region.Position.Y * scale;
                double rw = z.Region.Width * scale;
                double rh = z.Region.Height * scale;

                var rect = new Rectangle
                {
                    Width = Math.Max(1, rw),
                    Height = Math.Max(1, rh),
                    Fill = ZoneColors[i % ZoneColors.Length],
                    Stroke = Brushes.DarkGray,
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(rect, rx);
                Canvas.SetTop(rect, ry);
                ZoneCanvas.Children.Add(rect);
                _zoneRects[i] = rect;

                // Label
                var label = new TextBlock
                {
                    Text = z.Key,
                    FontSize = Math.Max(10, Math.Min(rw, rh) * 0.3),
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, rx + (rw - label.DesiredSize.Width) / 2);
                Canvas.SetTop(label, ry + (rh - label.DesiredSize.Height) / 2);
                ZoneCanvas.Children.Add(label);
            }
        }

        #region Event Handlers

        private void KeyCountChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _suppressEvents || KeyCountCombo.SelectedItem == null) return;
            var item = (ComboBoxItem)KeyCountCombo.SelectedItem;
            int count;
            if (int.TryParse(item.Content.ToString(), out count))
            {
                _keyCount = count;
                RebuildZones();
            }
        }

        private void LayoutChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _suppressEvents) return;
            _vertical = LayoutCombo.SelectedIndex == 0;
            RebuildZones();
        }

        private void PresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _suppressEvents || _touchpadBounds == null) return;

            double tw = _touchpadBounds.Width;
            double th = _touchpadBounds.Height;

            switch (PresetCombo.SelectedIndex)
            {
                case 0: // Full Area
                    _zoneRegion = new Area(tw, th, new Point(0, 0));
                    break;
                case 1: // Bottom Half
                    _zoneRegion = new Area(tw, th / 2, new Point(0, th / 2));
                    break;
                case 2: // Center 75%
                    _zoneRegion = new Area(tw * 0.75, th * 0.75, new Point(tw * 0.125, th * 0.125));
                    break;
            }

            RebuildZones();
        }

        private void ZoneCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawZones();
        }

        public void HighlightZones(int[] activeIndices)
        {
            if (_zoneRects == null) return;
            var active = new HashSet<int>(activeIndices);
            for (int i = 0; i < _zoneRects.Length; i++)
            {
                if (_zoneRects[i] == null) continue;
                bool on = active.Contains(i);
                _zoneRects[i].Fill = on ? ZoneActiveColors[i % ZoneActiveColors.Length] : ZoneColors[i % ZoneColors.Length];
                _zoneRects[i].StrokeThickness = on ? 3 : 1;
                _zoneRects[i].Stroke = on ? Brushes.White : Brushes.DarkGray;
            }
        }

        private void StartMania(object sender, RoutedEventArgs e)
        {
            var h = StartRequested;
            if (h != null) h();
        }

        private void StopMania(object sender, RoutedEventArgs e)
        {
            var h = StopRequested;
            if (h != null) h();
        }

        private void ResetZones(object sender, RoutedEventArgs e)
        {
            if (_touchpadBounds == null) return;
            // Reset zone region based on current preset selection
            double tw = _touchpadBounds.Width;
            double th = _touchpadBounds.Height;
            switch (PresetCombo.SelectedIndex)
            {
                case 1: // Bottom Half
                    _zoneRegion = new Area(tw, th / 2, new Point(0, th / 2));
                    break;
                case 2: // Center 75%
                    _zoneRegion = new Area(tw * 0.75, th * 0.75, new Point(tw * 0.125, th * 0.125));
                    break;
                default: // Full Area
                    _zoneRegion = new Area(tw, th, new Point(0, 0));
                    break;
            }
            RebuildZones();
        }

        public void SetRunningState(bool running)
        {
            ManiaStartButton.IsEnabled = !running;
            ManiaStopButton.IsEnabled = running;
        }

        #endregion
    }
}
