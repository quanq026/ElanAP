using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace ElanAP.Windows
{
    public partial class AboutBox : Window
    {
        public AboutBox()
        {
            InitializeComponent();

            Version.Content = Info.AssemblyVersion;
            Website.Content = Info.GitHub;
        }

        void CloseButton(object sender, RoutedEventArgs e) { Close(); }

        void ChangeLogButton(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo((string)Website.Content + @"/releases/tag/v" + Version.Content) { UseShellExecute = true }); }
            catch { }
        }

        void OpenWebsite(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo((string)Website.Content) { UseShellExecute = true }); }
            catch { }
        }

        void OpenContextMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Control)
            {
                var control = (Control)sender;
                if (control.ContextMenu != null)
                    control.ContextMenu.IsOpen = true;
            }
        }
    }
}
