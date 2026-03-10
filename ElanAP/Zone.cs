using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ElanAP
{
    public class Zone : INotifyPropertyChanged
    {
        public Zone()
        {
            Key = "Z";
            Region = new Area();
        }

        public Zone(Area region, string key)
        {
            Region = region;
            Key = key;
        }

        public Area Region
        {
            set { _region = value; NotifyPropertyChanged(); }
            get { return _region; }
        }
        private Area _region;

        public string Key
        {
            set { _key = value; NotifyPropertyChanged(); }
            get { return _key; }
        }
        private string _key;

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
