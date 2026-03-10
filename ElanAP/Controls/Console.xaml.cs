using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ElanAP.Controls
{
    public partial class Console : UserControl
    {
        public Console()
        {
            InitializeComponent();
        }

        public static string Prefix
        {
            get { return DateTime.Now.ToLocalTime() + ": "; }
        }

        public bool BufferEmpty
        {
            get { return Buffer.Text == string.Empty || Buffer.Text == null; }
        }

        public event EventHandler<string> Status;

        public Task Log(string text)
        {
            string line = Prefix + text;
            App.WriteLog(text);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (BufferEmpty)
                    Buffer.Text += line;
                else
                    Buffer.Text += Environment.NewLine + line;
                Buffer.CaretIndex = Buffer.Text.Length;
                Scroller.ScrollToEnd();
                var sh = Status;
                if (sh != null) sh(this, text);
            }));
            return Task.CompletedTask;
        }

        public async void Log(object sender, string text)
        {
            await Log(sender.GetType().Name + " - " + text);
        }

        public Task Clear()
        {
            Dispatcher.Invoke(new Action(() =>
            {
                Buffer.Text = string.Empty;
            }));
            return Task.CompletedTask;
        }

        public void CopyAll(object sender, EventArgs e) { Clipboard.SetText(Buffer.Text); }
        public void CopySelection(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(Buffer.SelectedText))
                Clipboard.SetText(Buffer.SelectedText);
        }
        public async void Clear(object sender, EventArgs e) { await Clear(); }
    }
}
