using System.Windows;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
    }
}
