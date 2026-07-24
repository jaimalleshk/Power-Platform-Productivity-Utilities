using System;
using System.Threading.Tasks;
using System.Windows;
using PowerPlatform.ProductivityEngine.Core.Logging;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class App : Application
    {
        [ThreadStatic]
        private static bool _inHandler;

        private static void SafeLog(string category, string message, Exception ex)
        {
            if (_inHandler) return;
            try
            {
                _inHandler = true;
                AppLogger.LogError(category, message, ex);
            }
            catch
            {
                // Swallow exceptions during logging to prevent infinite crash loops
            }
            finally
            {
                _inHandler = false;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            // Global Unhandled Exception Handlers - GUARANTEES APP NEVER CRASHES
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true; // Set handled IMMEDIATELY before attempting logging
            SafeLog("WPF UI Engine", $"Unhandled Dispatcher Exception caught: {e.Exception.Message}", e.Exception);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                SafeLog("AppDomain Engine", $"Unhandled Domain Exception caught: {ex.Message}", ex);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved(); // Set observed IMMEDIATELY before attempting logging
            if (e.Exception != null)
            {
                SafeLog("Task Engine", $"Unobserved Task Exception caught: {e.Exception.Message}", e.Exception);
            }
        }
    }
}
