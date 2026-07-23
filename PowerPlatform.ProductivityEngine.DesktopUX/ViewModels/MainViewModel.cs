using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PowerPlatform.ProductivityEngine.Core.Authentication;
using PowerPlatform.ProductivityEngine.Core.Connections;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.EnvironmentComparator.Engine;
using Utilities.EnvironmentComparator.Models;
using Utilities.EnvironmentComparator.Storage;

namespace PowerPlatform.ProductivityEngine.DesktopUX.ViewModels
{
    public class SelectableEnv : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private bool _isSimulationMode = true;
        private string _statusMessage = "Ready. Select 1 environment for Exploration or 2+ for Comparison.";
        private string _userEmail = string.Empty;
        private int _totalItems;
        private int _identicalCount;
        private int _deltaCount;
        private int _uniqueCount;
        private DiffNode? _selectedNode;

        public ObservableCollection<SelectableEnv> DiscoveredEnvironments { get; } = new();
        public ObservableCollection<DiffNode> UnifiedSolutionExplorerTree { get; } = new();
        public ObservableCollection<PropertyDiff> SelectedNodeProperties { get; } = new();

        public ComparisonScope Scope { get; } = new ComparisonScope();

        public bool IsSimulationMode
        {
            get => _isSimulationMode;
            set { _isSimulationMode = value; OnPropertyChanged(); }
        }

        public string UserEmail
        {
            get => _userEmail;
            set { _userEmail = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public int TotalItems { get => _totalItems; set { _totalItems = value; OnPropertyChanged(); } }
        public int IdenticalCount { get => _identicalCount; set { _identicalCount = value; OnPropertyChanged(); } }
        public int DeltaCount { get => _deltaCount; set { _deltaCount = value; OnPropertyChanged(); } }
        public int UniqueCount { get => _uniqueCount; set { _uniqueCount = value; OnPropertyChanged(); } }

        public DiffNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                _selectedNode = value;
                OnPropertyChanged();
                UpdateSelectedNodeProperties();
            }
        }

        public ICommand DiscoverEnvironmentsCommand { get; }
        public ICommand CompareEnvironmentsCommand { get; }
        public ICommand ExpandAllCommand { get; }
        public ICommand CollapseAllCommand { get; }
        public ICommand ExportHtmlCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand SaveToSqliteCommand { get; }

        private ComparisonResult? _lastResult;
        private List<RawEnvData> _lastRawEnvDataList = new();

        public MainViewModel()
        {
            DiscoverEnvironmentsCommand = new RelayCommand(async _ => await DiscoverEnvironmentsAsync());
            CompareEnvironmentsCommand = new RelayCommand(async _ => await CompareEnvironmentsAsync());
            ExpandAllCommand = new RelayCommand(_ => SetTreeExpandedState(UnifiedSolutionExplorerTree, true));
            CollapseAllCommand = new RelayCommand(_ => SetTreeExpandedState(UnifiedSolutionExplorerTree, false));

            ExportHtmlCommand = new RelayCommand(_ => ExportHtml());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv());
            ExportExcelCommand = new RelayCommand(_ => ExportExcel());
            SaveToSqliteCommand = new RelayCommand(_ => SaveToSqlite());

            // Initialize default mock environments for quick offline demo
            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-dev", Url = "https://contoso-dev.crm.dynamics.com", IsSelected = true });
            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-test", Url = "https://contoso-test.crm.dynamics.com", IsSelected = true });
            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-prod", Url = "https://contoso-prod.crm.dynamics.com", IsSelected = true });
        }

        public async Task DiscoverEnvironmentsAsync()
        {
            if (IsSimulationMode)
            {
                StatusMessage = "[SIMULATION] Populating demo environments (contoso-dev, test, prod)...";
                await Task.Delay(200);
                if (DiscoveredEnvironments.Count == 0)
                {
                    DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-dev", Url = "https://contoso-dev.crm.dynamics.com", IsSelected = true });
                    DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-test", Url = "https://contoso-test.crm.dynamics.com", IsSelected = true });
                    DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-prod", Url = "https://contoso-prod.crm.dynamics.com", IsSelected = true });
                }
                StatusMessage = "Discovered 3 simulated environments. Select target environments to explore or compare.";
                return;
            }

            // Real OAuth Discovery - Open Credentials Dialog
            var dialog = new CredentialDialog
            {
                Owner = Application.Current?.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                UserEmail = dialog.Username;
                StatusMessage = $"Authenticating as {UserEmail} via MSAL Azure AD...";

                try
                {
                    var authProvider = new MsalAuthenticationProvider(username: UserEmail, tenantId: dialog.TenantId);
                    StatusMessage = $"Discovering Dataverse environments for tenant ({UserEmail})...";

                    var envs = await authProvider.DiscoverEnvironmentsAsync().ConfigureAwait(true);

                    DiscoveredEnvironments.Clear();
                    foreach (var env in envs)
                    {
                        DiscoveredEnvironments.Add(new SelectableEnv
                        {
                            Name = env.Name,
                            Url = env.Url,
                            IsSelected = true
                        });
                    }

                    StatusMessage = $"Discovered {DiscoveredEnvironments.Count} real tenant environments for {UserEmail}.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Authentication or Environment Discovery failed:\n{ex.Message}", "OAuth Discovery Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = $"Discovery error: {ex.Message}";
                }
            }
        }

        public async Task CompareEnvironmentsAsync()
        {
            var selectedEnvs = DiscoveredEnvironments.Where(e => e.IsSelected).ToList();
            if (selectedEnvs.Count == 0)
            {
                StatusMessage = "Please select at least 1 environment.";
                return;
            }

            bool isSingleEnv = selectedEnvs.Count == 1;
            StatusMessage = isSingleEnv 
                ? $"Exploring single environment ({selectedEnvs[0].Name})..." 
                : $"Comparing {selectedEnvs.Count} environments via D365 Web API / OAuth...";

            UnifiedSolutionExplorerTree.Clear();

            var profiles = selectedEnvs.Select(e => new ConnectionProfile
            {
                EnvironmentUrl = e.Url,
                UseInteractiveAuth = true
            }).ToList();

            Scope.TargetEnvironments = profiles;

            var crawler = new EnvironmentMetadataCrawler(useSimulationMode: IsSimulationMode);
            _lastRawEnvDataList.Clear();

            var progress = new Progress<ProgressUpdate>(p => {
                StatusMessage = $"[{p.Stage}] {p.Message}";
            });

            foreach (var profile in profiles)
            {
                var data = await crawler.CrawlEnvironmentAsync(profile, Scope, progress);
                _lastRawEnvDataList.Add(data);
            }

            var comparer = new NWayComparer();
            _lastResult = comparer.CompareEnvironments(_lastRawEnvDataList, Scope);

            // Build Unified Root 1 & Root 2 Tree
            var root1Folder = new DiffNode
            {
                RootCategory = RootCategory.AdminSettings,
                SubCategory = "Folder",
                DisplayName = "📁 Root 1: Admin & Environment Settings (OrgDbOrgSettings, Security, EnvVars)",
                UniqueKey = "Root1.AdminSettings"
            };
            foreach (var n in _lastResult.AdminSettingsNodes) root1Folder.Children.Add(n);

            var root2Folder = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Folder",
                DisplayName = "📁 Root 2: Solution Explorer & Customizations (Solutions, Apps, Tables, Plugins)",
                UniqueKey = "Root2.SolutionExplorer"
            };
            foreach (var n in _lastResult.MetadataNodes) root2Folder.Children.Add(n);

            UnifiedSolutionExplorerTree.Add(root1Folder);
            UnifiedSolutionExplorerTree.Add(root2Folder);

            TotalItems = _lastResult.TotalCount;
            IdenticalCount = _lastResult.IdenticalCount;
            DeltaCount = _lastResult.DeltaCount;
            UniqueCount = _lastResult.UniqueCount;

            StatusMessage = isSingleEnv
                ? $"Exploration complete for {selectedEnvs[0].Name}. Total components: {TotalItems}."
                : $"Comparison complete across {selectedEnvs.Count} environments. Found {_lastResult.DeltaCount} Deltas, {_lastResult.UniqueCount} Unique items.";
        }

        private void SetTreeExpandedState(ObservableCollection<DiffNode> nodes, bool isExpanded)
        {
            foreach (var node in nodes)
            {
                node.IsExpanded = isExpanded;
                if (node.Children.Count > 0)
                {
                    SetTreeExpandedState(node.Children, isExpanded);
                }
            }
        }

        private void UpdateSelectedNodeProperties()
        {
            SelectedNodeProperties.Clear();
            if (_selectedNode == null || _selectedNode.PropertyDiffs == null) return;

            foreach (var prop in _selectedNode.PropertyDiffs)
            {
                SelectedNodeProperties.Add(prop);
            }
        }

        private void ExportHtml()
        {
            if (_lastResult == null) return;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "EnvComparison_Dashboard.html");
            var exporter = new ComparatorExporter();
            exporter.ExportToHtml(path, _lastResult);
            StatusMessage = $"Exported Glassmorphic HTML Dashboard to {path}";
        }

        private void ExportCsv()
        {
            if (_lastResult == null) return;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "EnvComparison_Matrix.csv");
            var exporter = new ComparatorExporter();
            exporter.ExportToCsvExcel(path, _lastResult);
            StatusMessage = $"Exported Comparison Matrix CSV to {path}";
        }

        private void ExportExcel()
        {
            if (_lastResult == null) return;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "EnvComparison_MultiWorksheet.xml");
            var exporter = new ComparatorExporter();
            exporter.ExportFormattedExcel(path, _lastResult);
            StatusMessage = $"Exported Formatted Multi-Worksheet Excel Report to {path}";
        }

        private void SaveToSqlite()
        {
            if (_lastRawEnvDataList.Count == 0) return;
            string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "env_snapshots.sqlite");
            var storageEngine = new OfflineStorageEngine();
            foreach (var envData in _lastRawEnvDataList)
            {
                storageEngine.SaveSnapshot(dbPath, envData);
            }
            StatusMessage = $"Saved {_lastRawEnvDataList.Count} environment snapshots to offline SQLite DB at {dbPath}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged;
    }
}
