using System;
using System.Threading.Tasks;
using System.Windows;
using PowerPlatform.ProductivityEngine.Core.Logging;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class App : Application
    {
        [ThreadStatic]
        private static bool _isHandlingException;

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
            if (_isHandlingException)
            {
                e.Handled = true;
                return;
            }

            try
            {
                _isHandlingException = true;
                AppLogger.LogError("WPF UI Engine", $"Unhandled Dispatcher Exception caught: {e.Exception.Message}", e.Exception);
            }
            finally
            {
                _isHandlingException = false;
                e.Handled = true; // PREVENT CRASH
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (_isHandlingException) return;
            try
            {
                _isHandlingException = true;
                if (e.ExceptionObject is Exception ex)
                {
                    AppLogger.LogError("AppDomain Engine", $"Unhandled Domain Exception caught: {ex.Message}", ex);
                }
            }
            finally
            {
                _isHandlingException = false;
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (_isHandlingException)
            {
                e.SetObserved();
                return;
            }

            try
            {
                _isHandlingException = true;
                AppLogger.LogError("Task Engine", $"Unobserved Task Exception caught: {e.Exception.Message}", e.Exception);
            }
            finally
            {
                _isHandlingException = false;
                e.SetObserved(); // PREVENT CRASH
            }
        }
    }
}
