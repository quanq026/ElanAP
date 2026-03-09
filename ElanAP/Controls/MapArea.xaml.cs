using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ElanAP.Controls
{
    public partial class MapArea : UserControl
    {
        public MapArea()
        {
            InitializeComponent();
            CreateCanvasObjects();
        }

        public event EventHandler<string> Output;
        public event EventHandler<Area> AreaDragged;

        [Bindable(true), Category("Common")]
        public Area ForegroundArea
        {
            set
            {
                SetValue(ForegroundAreaProperty, value);
                UpdateCanvas();
            }
            get
            {
                return (Area)GetValue(ForegroundAreaProperty);
            }
        }

        [Bindable(true), Category("Common")]
        public Area BackgroundArea
        {
            set
            {
                SetValue(BackgroundAreaProperty, value);
                UpdateCanvas();
            }
            get
            {
                return (Area)GetValue(BackgroundAreaProperty);
            }
        }

        private Rectangle foreground;
        private Rectangle background;
        private Rectangle _dragRect;
        private bool _isDragging;
        private System.Windows.Point _dragStartCanvas;
        private double _scale;
        private Point _centerOffset;

        public static readonly DependencyProperty ForegroundAreaProperty = DependencyProperty.Register(
            "ForegroundArea", typeof(Area), typeof(MapArea));

        public static readonly DependencyProperty BackgroundAreaProperty = DependencyProperty.Register(
            "BackgroundArea", typeof(Area), typeof(MapArea));

        private void CreateCanvasObjects()
        {
            AreaCanvas.Children.Clear();

            foreground = new Rectangle
            {
                Fill = SystemParameters.WindowGlassBrush ?? Brushes.SkyBlue,
            };
            AreaCanvas.Children.Add(foreground);

            background = new Rectangle
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1.0,
                Fill = Brushes.Transparent
            };
            AreaCanvas.Children.Add(background);

            _dragRect = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }),
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
                Visibility = Visibility.Collapsed
            };
            AreaCanvas.Children.Add(_dragRect);
        }

        public void UpdateCanvas()
        {
            try
            {
                double scaleX = this.ActualWidth / BackgroundArea.Width;
                double scaleY = this.ActualHeight / BackgroundArea.Height;
                _scale = Math.Min(scaleX, scaleY);

                _centerOffset = new Point
                {
                    X = this.ActualWidth / 2.0 - BackgroundArea.Width * _scale / 2.0,
                    Y = this.ActualHeight / 2.0 - BackgroundArea.Height * _scale / 2.0,
                };

                background.Width = BackgroundArea.Width * _scale;
                background.Height = BackgroundArea.Height * _scale;
                MoveObject(background, _centerOffset);

                foreground.Width = ForegroundArea.Width * _scale;
                foreground.Height = ForegroundArea.Height * _scale;
                MoveObject(foreground, new Point(
                    _centerOffset.X + ForegroundArea.Position.X * _scale,
                    _centerOffset.Y + ForegroundArea.Position.Y * _scale));
            }
            catch
            {
                // Invalid or uninitialized settings
            }
        }

        #region Drag-to-Select

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_scale <= 0) return;

            _dragStartCanvas = e.GetPosition(AreaCanvas);
            _isDragging = true;
            _dragRect.Width = 0;
            _dragRect.Height = 0;
            _dragRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(_dragRect, _dragStartCanvas.X);
            Canvas.SetTop(_dragRect, _dragStartCanvas.Y);
            AreaCanvas.CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isDragging) return;

            System.Windows.Point current = e.GetPosition(AreaCanvas);

            double x = Math.Min(_dragStartCanvas.X, current.X);
            double y = Math.Min(_dragStartCanvas.Y, current.Y);
            double w = Math.Abs(current.X - _dragStartCanvas.X);
            double h = Math.Abs(current.Y - _dragStartCanvas.Y);

            Canvas.SetLeft(_dragRect, x);
            Canvas.SetTop(_dragRect, y);
            _dragRect.Width = Math.Max(0, w);
            _dragRect.Height = Math.Max(0, h);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_isDragging) return;
            _isDragging = false;
            AreaCanvas.ReleaseMouseCapture();
            _dragRect.Visibility = Visibility.Collapsed;

            if (_scale <= 0) return;

            System.Windows.Point endCanvas = e.GetPosition(AreaCanvas);
            double cx1 = Math.Min(_dragStartCanvas.X, endCanvas.X);
            double cy1 = Math.Min(_dragStartCanvas.Y, endCanvas.Y);
            double cx2 = Math.Max(_dragStartCanvas.X, endCanvas.X);
            double cy2 = Math.Max(_dragStartCanvas.Y, endCanvas.Y);

            // Canvas -> area coordinates
            double ax1 = (cx1 - _centerOffset.X) / _scale;
            double ay1 = (cy1 - _centerOffset.Y) / _scale;
            double ax2 = (cx2 - _centerOffset.X) / _scale;
            double ay2 = (cy2 - _centerOffset.Y) / _scale;

            // Clamp to background bounds
            ax1 = Math.Max(0, Math.Min(ax1, BackgroundArea.Width));
            ay1 = Math.Max(0, Math.Min(ay1, BackgroundArea.Height));
            ax2 = Math.Max(0, Math.Min(ax2, BackgroundArea.Width));
            ay2 = Math.Max(0, Math.Min(ay2, BackgroundArea.Height));

            double w = ax2 - ax1;
            double h = ay2 - ay1;
            if (w < 10 || h < 10) return; // too small, ignore

            Area newArea = new Area
            {
                Width = w,
                Height = h,
                Position = new Point { X = ax1, Y = ay1 }
            };

            var handler = AreaDragged;
            if (handler != null) handler(this, newArea);
        }

        #endregion

        public Task MoveObject(UIElement obj, Point position)
        {
            Canvas.SetLeft(obj, position.X);
            Canvas.SetTop(obj, position.Y);
            return Task.CompletedTask;
        }

        public Task LazyAreaUpdate(Area foreground)
        {
            ForegroundArea = foreground;
            return Task.CompletedTask;
        }
    }
}
