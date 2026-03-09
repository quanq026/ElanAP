using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ElanAP
{
    public class Area : INotifyPropertyChanged
    {
        public Area()
        {
            Width = 0;
            Height = 0;
            Position = new Point(0, 0);
        }

        public Area(double width, double height)
        {
            Width = width;
            Height = height;
            Position = new Point(0, 0);
        }

        public Area(double width, double height, Point pos) : this(width, height)
        {
            Position = pos;
        }

        public Area(string area)
        {
            var properties = area.Split(',');
            Width = Convert.ToDouble(properties[0]);
            Height = Convert.ToDouble(properties[1]);
            var x = Convert.ToDouble(properties[2]);
            var y = Convert.ToDouble(properties[3]);
            Position = new Point(x, y);
        }

        public Area(System.Drawing.Rectangle area) : this(area.Width, area.Height) { }

        public Area(System.Drawing.Rectangle area, Point pos) : this(area)
        {
            Position = pos;
        }

        public double Width
        {
            set
            {
                _width = value;
                NotifyPropertyChanged();
            }
            get { return _width; }
        }
        private double _width;

        public double Height
        {
            set
            {
                _height = value;
                NotifyPropertyChanged();
            }
            get { return _height; }
        }
        private double _height;

        public Point Position
        {
            set
            {
                _position = value;
                NotifyPropertyChanged();
            }
            get { return _position; }
        }
        private Point _position;

        public override string ToString()
        {
            return Width + "," + Height + "," + Position.X + "," + Position.Y;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string PropertyName = "")
        {
            if (PropertyName != null)
            {
                var h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs(PropertyName));
            }
        }
    }
}
