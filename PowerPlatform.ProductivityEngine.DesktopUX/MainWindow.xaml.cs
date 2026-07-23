using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Utilities.EnvironmentComparator.Models;
using PowerPlatform.ProductivityEngine.DesktopUX.ViewModels;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.InfoConsoleLogs.CollectionChanged += (s, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Add)
                    {
                        Dispatcher.BeginInvoke(() => InfoConsoleScrollViewer?.ScrollToEnd());
                    }
                };

                vm.ErrorConsoleLogs.CollectionChanged += (s, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Add)
                    {
                        Dispatcher.BeginInvoke(() => ErrorConsoleScrollViewer?.ScrollToEnd());
                    }
                };
            }
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
