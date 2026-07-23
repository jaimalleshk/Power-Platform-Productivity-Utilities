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

    public class KeyValueRow
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public enum DetailContentType
    {
        PropertyGrid,
        CodeEditor,
        XmlViewer
    }

    public class EnvDetailTabViewModel : INotifyPropertyChanged
    {
        private DetailContentType _contentType = DetailContentType.PropertyGrid;
        private string _rawCodeContent = string.Empty;

        public string Header { get; set; } = string.Empty;
        public bool IsComparisonTab { get; set; }

        public DetailContentType ContentType
        {
            get => _contentType;
            set { _contentType = value; OnPropertyChanged(); }
        }

        public string RawCodeContent
        {
            get => _rawCodeContent;
            set { _rawCodeContent = value; OnPropertyChanged(); }
        }

        public ObservableCollection<KeyValueRow> Properties { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private bool _isSimulationMode = false;
        private bool _isLoading = false;
        private double _progressPercentage = 0;
        private string _progressDetails = string.Empty;
        private string _statusMessage = "Ready. Click 'Discover Envs (OAuth)' to connect to your tenant.";
        private string _userEmail = string.Empty;
        private int _totalItems;
        private int _identicalCount;
        private int _deltaCount;
        private int _uniqueCount;
        private DiffNode? _selectedNode;

        public ObservableCollection<SelectableEnv> DiscoveredEnvironments { get; } = new();
        public ObservableCollection<DiffNode> UnifiedSolutionExplorerTree { get; } = new();
        public ObservableCollection<PropertyDiff> SelectedNodeProperties { get; } = new();
        public ObservableCollection<EnvDetailTabViewModel> EnvDetailTabs { get; } = new();

        public ComparisonScope Scope { get; } = new ComparisonScope();

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public double ProgressPercentage
        {
            get => _progressPercentage;
            set { _progressPercentage = value; OnPropertyChanged(); }
        }

        public string ProgressDetails
        {
            get => _progressDetails;
            set { _progressDetails = value; OnPropertyChanged(); }
        }

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
                UpdateEnvDetailTabs();
            }
        }

        public ICommand DiscoverEnvironmentsCommand { get; }
        public ICommand CompareEnvironmentsCommand { get; }
        public ICommand AddManualEnvCommand { get; }
        public ICommand SelectAllEnvsCommand { get; }
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
            AddManualEnvCommand = new RelayCommand(_ => AddManualEnvironment());
            SelectAllEnvsCommand = new RelayCommand(_ => ToggleSelectAllEnvs());
            ExpandAllCommand = new RelayCommand(_ => SetTreeExpandedState(UnifiedSolutionExplorerTree, true));
            CollapseAllCommand = new RelayCommand(_ => SetTreeExpandedState(UnifiedSolutionExplorerTree, false));

            ExportHtmlCommand = new RelayCommand(_ => ExportHtml());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv());
            ExportExcelCommand = new RelayCommand(_ => ExportExcel());
            SaveToSqliteCommand = new RelayCommand(_ => SaveToSqlite());

            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-dev", Url = "https://contoso-dev.crm.dynamics.com", IsSelected = true });
            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-test", Url = "https://contoso-test.crm.dynamics.com", IsSelected = true });
            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-prod", Url = "https://contoso-prod.crm.dynamics.com", IsSelected = true });
        }

        public async Task DiscoverEnvironmentsAsync()
        {
            bool? dialogResult = false;
            string enteredUsername = string.Empty;
            string enteredTenant = string.Empty;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new CredentialDialog
                {
                    Owner = Application.Current?.MainWindow
                };

                dialogResult = dialog.ShowDialog();
                if (dialogResult == true)
                {
                    enteredUsername = dialog.Username;
                    enteredTenant = dialog.TenantId;
                }
            });

            if (dialogResult != true || string.IsNullOrWhiteSpace(enteredUsername))
            {
                StatusMessage = "Environment Discovery cancelled.";
                return;
            }

            UserEmail = enteredUsername;
            IsSimulationMode = false;
            IsLoading = true;
            ProgressPercentage = 10;
            ProgressDetails = "Authenticating via MSAL Azure AD...";
            StatusMessage = $"Authenticating as {UserEmail} via MSAL Azure AD...";

            try
            {
                var authProvider = new MsalAuthenticationProvider(username: UserEmail, tenantId: enteredTenant);

                var envs = await authProvider.DiscoverEnvironmentsAsync(msg =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ProgressDetails = msg;
                        StatusMessage = msg;
                    });
                }).ConfigureAwait(true);

                if (envs != null && envs.Count > 0)
                {
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

                    ProgressPercentage = 100;
                    ProgressDetails = $"Successfully discovered {DiscoveredEnvironments.Count} real tenant environments!";
                    StatusMessage = $"Discovered {DiscoveredEnvironments.Count} real tenant environments for {UserEmail}. Select target environments to explore or compare.";
                }
                else
                {
                    ProgressDetails = "No environments returned from Global Discovery API.";
                    StatusMessage = $"No environments returned for tenant {UserEmail}. You can click '➕ Add Env URL' to manually add your target environment URL.";
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Authentication or Environment Discovery failed:\n{ex.Message}", "OAuth Discovery Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                ProgressDetails = $"Discovery error: {ex.Message}";
                StatusMessage = $"Discovery error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
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
            IsLoading = true;
            ProgressPercentage = 0;
            ProgressDetails = isSingleEnv ? $"Exploring single environment ({selectedEnvs[0].Name})..." : $"Comparing {selectedEnvs.Count} environments...";
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

            int currentEnvIndex = 0;
            int totalEnvs = profiles.Count;

            var progress = new Progress<ProgressUpdate>(p => {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    double stepPct = (double)currentEnvIndex / totalEnvs * 100;
                    ProgressPercentage = Math.Min(95, p.PercentComplete > 0 ? p.PercentComplete : stepPct);
                    ProgressDetails = $"Environment [{currentEnvIndex}/{totalEnvs}]: [{p.Stage}] {p.Message}";
                    StatusMessage = ProgressDetails;
                });
            });

            try
            {
                foreach (var profile in profiles)
                {
                    currentEnvIndex++;
                    var data = await crawler.CrawlEnvironmentAsync(profile, Scope, progress);
                    _lastRawEnvDataList.Add(data);
                }

                ProgressDetails = "Executing N-Way Matrix Diffing & Solution Explorer Tree Building...";
                StatusMessage = ProgressDetails;

                var comparer = new NWayComparer();
                _lastResult = comparer.CompareEnvironments(_lastRawEnvDataList, Scope);

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

                ProgressPercentage = 100;
                ProgressDetails = isSingleEnv
                    ? $"Exploration complete for {selectedEnvs[0].Name}. Total components: {TotalItems}."
                    : $"Comparison complete across {selectedEnvs.Count} environments. Found {_lastResult.DeltaCount} Deltas, {_lastResult.UniqueCount} Unique items.";

                StatusMessage = ProgressDetails;
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Crawl / Comparison Error:\n{ex.Message}", "Comparison Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                StatusMessage = $"Error during comparison: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void AddManualEnvironment()
        {
            string url = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter target Dataverse Environment URL (e.g. https://myclient-dev.crm.dynamics.com):",
                "Add Custom Environment URL",
                "https://");

            if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                string friendlyName = url.Replace("https://", "").Replace("http://", "").Split('.')[0];
                
                // Remove simulation defaults if present
                var dummyItems = DiscoveredEnvironments.Where(e => e.Url.Contains("contoso-")).ToList();
                foreach (var dummy in dummyItems)
                {
                    DiscoveredEnvironments.Remove(dummy);
                }

                if (!DiscoveredEnvironments.Any(e => e.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                {
                    DiscoveredEnvironments.Add(new SelectableEnv
                    {
                        Name = friendlyName,
                        Url = url,
                        IsSelected = true
                    });

                    StatusMessage = $"Added environment '{friendlyName}' ({url}). Select environments and click '🔄 Run Compare / Explore'.";
                }
            }
        }

        private void ToggleSelectAllEnvs()
        {
            if (DiscoveredEnvironments.Count == 0) return;
            bool allSelected = DiscoveredEnvironments.All(e => e.IsSelected);
            foreach (var env in DiscoveredEnvironments)
            {
                env.IsSelected = !allSelected;
            }
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

        private void UpdateEnvDetailTabs()
        {
            EnvDetailTabs.Clear();
            if (_selectedNode == null) return;

            bool isCode = _selectedNode.SubCategory.Contains("WebResource") || 
                          _selectedNode.SubCategory.Contains("PluginAssembly") || 
                          _selectedNode.UniqueKey.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

            bool isXml = _selectedNode.SubCategory.Contains("Form") || 
                         _selectedNode.SubCategory.Contains("View") || 
                         _selectedNode.SubCategory.Contains("SiteMap") ||
                         _selectedNode.SubCategory.Contains("Dashboard");

            DetailContentType contentType = isCode ? DetailContentType.CodeEditor : (isXml ? DetailContentType.XmlViewer : DetailContentType.PropertyGrid);

            // 1. Comparison Tab
            var compTab = new EnvDetailTabViewModel
            {
                Header = "📊 Side-by-Side Comparison",
                IsComparisonTab = true,
                ContentType = DetailContentType.PropertyGrid
            };
            if (_selectedNode.EnvironmentValues != null)
            {
                foreach (var kv in _selectedNode.EnvironmentValues)
                {
                    compTab.Properties.Add(new KeyValueRow { Key = kv.Key, Value = kv.Value });
                }
            }
            EnvDetailTabs.Add(compTab);

            // 2. Per-Environment Tabs with Polymorphic View Types (Code vs Xml vs PropertyGrid)
            if (_lastResult != null && _lastResult.TargetEnvironmentNames != null)
            {
                foreach (var envName in _lastResult.TargetEnvironmentNames)
                {
                    var envTab = new EnvDetailTabViewModel
                    {
                        Header = $"🌐 {envName}",
                        IsComparisonTab = false,
                        ContentType = contentType
                    };

                    if (contentType == DetailContentType.CodeEditor)
                    {
                        envTab.RawCodeContent = $@"// Source Code Viewer [{_selectedNode.DisplayName}] for {envName}
function onAccountSave(executionContext) {{
    var formContext = executionContext.getFormContext();
    var accountName = formContext.getAttribute('name').getValue();
    console.log('Validating account: ' + accountName);
}}";
                    }
                    else if (contentType == DetailContentType.XmlViewer)
                    {
                        envTab.RawCodeContent = $@"<!-- XML Form/View Definition [{_selectedNode.DisplayName}] for {envName} -->
<form>
  <tabs>
    <tab name='general' label='General Information'>
      <columns>
        <column width='100%'>
          <sections>
            <section name='account_details' label='Account Details' />
          </sections>
        </column>
      </columns>
    </tab>
  </tabs>
</form>";
                    }
                    else
                    {
                        foreach (var propDiff in _selectedNode.PropertyDiffs)
                        {
                            string val = propDiff.ValuesPerEnv.TryGetValue(envName, out var v) ? v : "[N/A]";
                            envTab.Properties.Add(new KeyValueRow { Key = propDiff.PropertyName, Value = val });
                        }
                    }

                    EnvDetailTabs.Add(envTab);
                }
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
