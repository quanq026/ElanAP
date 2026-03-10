using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ElanAP
{
    public partial class App : Application
    {
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "elanap.log");

        private static readonly object _logLock = new object();

        private static Mutex _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "ElanAP_SingleInstance_8F3A2B", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("ElanAP is already running.", "ElanAP", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // Catch unhandled exceptions on UI thread
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Catch unhandled exceptions on background threads
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            WriteLog("=== App started ===");
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteLog("CRASH (UI thread): " + e.Exception);
            e.Handled = false; // let it crash after logging
        }

        [HandleProcessCorruptedStateExceptions]
        [SecurityCritical]
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
