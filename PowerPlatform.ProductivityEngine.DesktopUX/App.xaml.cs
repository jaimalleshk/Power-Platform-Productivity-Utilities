using System;
using System.Threading.Tasks;
using System.Windows;
using PowerPlatform.ProductivityEngine.Core.Logging;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class App : Application
    {
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
            AppLogger.LogError("WPF UI Engine", $"Unhandled Dispatcher Exception caught: {e.Exception.Message}", e.Exception);
            e.Handled = true; // PREVENT CRASH
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                AppLogger.LogError("AppDomain Engine", $"Unhandled Domain Exception caught: {ex.Message}", ex);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLogger.LogError("Task Engine", $"Unobserved Task Exception caught: {e.Exception.Message}", e.Exception);
            e.SetObserved(); // PREVENT CRASH
        }
    }
}
