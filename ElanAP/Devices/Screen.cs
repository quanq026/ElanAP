namespace ElanAP.Devices
{
    public class Screen
    {
        public Screen()
        {
            Bounds = new Area(System.Windows.Forms.Screen.PrimaryScreen.Bounds);
        }

        public Area Bounds;
    }
}
