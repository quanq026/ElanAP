using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace ElanAP
{
    [XmlRoot("ElanAP Configuration", IsNullable = true)]
    public class Configuration : INotifyPropertyChanged
    {
        public Configuration()
        {
            Touchpad = new Area();
            Screen = new Area();
            ManiaZones = new List<Zone>();
        }

        public Configuration(Area touchpad, Area screen)
        {
            Touchpad = touchpad;
            Screen = screen;
            ManiaZones = new List<Zone>();
        }

        public Area Touchpad
        {
            set
            {
                _touchpad = value;
                NotifyPropertyChanged();
            }
            get { return _touchpad; }
        }
        private Area _touchpad;

        public Area Screen
        {
            set
            {
                _screen = value;
                NotifyPropertyChanged();
            }
            get { return _screen; }
        }
        private Area _screen;

        public bool LockAspectRatio
        {
            set
            {
                _lockaspectratio = value;
                NotifyPropertyChanged();
            }
            get { return _lockaspectratio; }
        }
        private bool _lockaspectratio;

        public List<Zone> ManiaZones
        {
            set
            {
                _maniaZones = value;
                NotifyPropertyChanged();
            }
            get { return _maniaZones; }
        }
        private List<Zone> _maniaZones;

        #region File Management

        private static XmlSerializer Serializer = new XmlSerializer(typeof(Configuration));

        public void Save(string path)
        {
            using (var tw = new StreamWriter(path))
            {
                Serializer.Serialize(tw, this);
            }
        }

        public static Configuration Read(string path)
        {
            using (var sr = new StreamReader(path))
                return (Configuration)Serializer.Deserialize(sr);
        }

        #endregion

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
