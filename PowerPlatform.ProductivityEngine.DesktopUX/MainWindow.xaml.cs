using System.Windows;
using Utilities.EnvironmentComparator.Models;
using PowerPlatform.ProductivityEngine.DesktopUX.ViewModels;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is MainViewModel vm && e.NewValue is DiffNode selectedNode)
            {
                vm.SelectedNode = selectedNode;
            }
        }
    }
}
