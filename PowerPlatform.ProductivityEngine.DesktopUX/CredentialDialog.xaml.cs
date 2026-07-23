using System.Linq;
using System.Windows;
using PowerPlatform.ProductivityEngine.Core.Authentication;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class CredentialDialog : Window
    {
        public string Username => CmbUsername.Text.Trim();
        public string TenantId => string.Empty; // Tenant ID auto-discovered automatically

        public CredentialDialog()
        {
            InitializeComponent();
            LoadSavedUsernames();
        }

        private void LoadSavedUsernames()
        {
            var settings = UserSettingsManager.LoadSettings();
            if (settings.SavedUsernames != null && settings.SavedUsernames.Count > 0)
            {
                foreach (var name in settings.SavedUsernames)
                {
                    CmbUsername.Items.Add(name);
                }

                if (!string.IsNullOrWhiteSpace(settings.LastUsedUsername))
                {
                    CmbUsername.Text = settings.LastUsedUsername;
                }
                else
                {
                    CmbUsername.SelectedIndex = 0;
                }
            }
        }

        private void OnSignInClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Please enter or select your Client User Email / UPN.", "Username Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Persist selected username into auto-complete history
            UserSettingsManager.SaveUsername(Username);

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
