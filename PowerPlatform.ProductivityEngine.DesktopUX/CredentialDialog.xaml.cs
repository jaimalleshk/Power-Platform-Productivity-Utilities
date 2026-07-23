using System.Windows;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class CredentialDialog : Window
    {
        public string Username => TxtUsername.Text.Trim();
        public string TenantId => TxtTenantId.Text.Trim();

        public CredentialDialog()
        {
            InitializeComponent();
        }

        private void OnSignInClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Please enter your Client User Email / UPN.", "Username Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
