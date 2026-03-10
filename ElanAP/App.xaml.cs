using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ElanAP
{
    public partial class App : Application
    {
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "elanap.log");

        private static readonly object _logLock = new object();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Catch unhandled exceptions on UI thread
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Catch unhandled exceptions on background threads
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            WriteLog("=== App started ===");
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteLog("CRASH (UI thread): " + e.Exception);
            e.Handled = false; // let it crash after logging
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteLog("CRASH (background): " + e.ExceptionObject);
        }

        public static void WriteLog(string message)
        {
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
