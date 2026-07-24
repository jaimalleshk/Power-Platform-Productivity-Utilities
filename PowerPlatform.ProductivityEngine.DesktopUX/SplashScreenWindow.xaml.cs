using System;
using System.Threading.Tasks;
using System.Windows;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class SplashScreenWindow : Window
    {
        public SplashScreenWindow()
        {
            InitializeComponent();
            Loaded += SplashScreenWindow_Loaded;
        }

        private async void SplashScreenWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RunInitializationStepsAsync();
        }

        private async Task RunInitializationStepsAsync()
        {
            void UpdateProgress(string message, int percent)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = message;
                    SplashProgressBar.Value = percent;
                });
            }

            UpdateProgress("🔑 Initializing Dataverse OAuth & MSAL Core Services...", 15);
            await Task.Delay(200);

            UpdateProgress("🗄️ Initializing SQLite Offline Caching Database...", 35);
            await Task.Delay(200);

            UpdateProgress("📁 Loading Power Platform 15-Category Solution Explorer Skeleton...", 60);
            await Task.Delay(250);

            UpdateProgress("📜 Starting Non-Blocking Background Logging Subsystem...", 85);
            await Task.Delay(200);

            UpdateProgress("🚀 Launching Power Platform Engine Suite...", 100);
            await Task.Delay(200);

            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }
    }
}
