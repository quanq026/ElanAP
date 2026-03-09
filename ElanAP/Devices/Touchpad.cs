namespace ElanAP.Devices
{
    public class Touchpad
    {
        public Touchpad()
        {
            X_Lo = 0;
            X_Hi = 3200;
            Y_Lo = 0;
            Y_Hi = 2000;
            Bounds = new Area(X_Hi - X_Lo, Y_Hi - Y_Lo);
        }

        public Touchpad(API api)
        {
            if (api.IsAvailable)
            {
                X_Lo = api.X_Lo;
                X_Hi = api.X_Hi;
                Y_Lo = api.Y_Lo;
                Y_Hi = api.Y_Hi;
                Bounds = new Area(X_Hi - X_Lo, Y_Hi - Y_Lo, new Point(X_Lo, Y_Lo));
            }
            else
            {
                X_Lo = 0;
                X_Hi = 3200;
                Y_Lo = 0;
                Y_Hi = 2000;
                Bounds = new Area(X_Hi - X_Lo, Y_Hi - Y_Lo);
            }
        }

        public Area Bounds;

        public int X_Lo;
        public int X_Hi;
        public int Y_Lo;
        public int Y_Hi;

        public override string ToString()
        {
            return "[" + Bounds + "],[" + X_Lo + "," + X_Hi + "|" + Y_Lo + "," + Y_Hi + "]";
        }
    }
}
