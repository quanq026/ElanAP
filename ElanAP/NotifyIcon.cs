using System;
using System.Reflection;
using System.Windows.Forms;
using FormsIcon = System.Windows.Forms.NotifyIcon;

namespace ElanAP
{
    public class NotifyIcon : IDisposable
    {
        public NotifyIcon()
        {
            string iconPath = @"ElanAP.Icon.ico";
            Assembly assembly = Assembly.GetExecutingAssembly();

            var stream = assembly.GetManifestResourceStream(iconPath);
            if (stream != null)
                Icon.Icon = new System.Drawing.Icon(stream);
            Icon.MouseClick += NotifyIcon_Click;
            Icon.Text = "ElanAP " + "v" + Info.AssemblyVersion;
        }

        FormsIcon Icon = new FormsIcon();

        public event EventHandler ShowWindow;

        public bool Visible
        {
            set { Icon.Visible = value; }
            get { return Icon.Visible; }
        }

        private void NotifyIcon_Click(object sender, MouseEventArgs e)
        {
            var h = ShowWindow;
            if (h != null) h(this, null);
        }

        public void Dispose()
        {
            Icon.Visible = false;
            Icon.Dispose();
        }
    }
}
