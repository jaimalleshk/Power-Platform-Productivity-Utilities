using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using PowerPlatform.ProductivityEngine.Core.Connections;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.EnvironmentComparator.Engine;
using Utilities.EnvironmentComparator.Models;

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

        public event PropertyChangedEventHandler? PropertyPropertyChanged;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private bool _isSimulationMode = true;
        private string _statusMessage = "Ready. Select 2 or more environments to compare.";
        private int _totalItems;
        private int _identicalCount;
        private int _deltaCount;
        private int _uniqueCount;
        private DiffNode? _selectedNode;

        public ObservableCollection<SelectableEnv> DiscoveredEnvironments { get; } = new();
        public ObservableCollection<DiffNode> AdminSettingsTree { get; } = new();
        public ObservableCollection<DiffNode> MetadataTree { get; } = new();
        public ObservableCollection<PropertyDiff> SelectedNodeProperties { get; } = new();

        public ComparisonScope Scope { get; } = new ComparisonScope();

        public bool IsSimulationMode
        {
            get => _isSimulationMode;
            set { _isSimulationMode = value; OnPropertyChanged(); }
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
        public ICommand ExportHtmlCommand { get; }
        public ICommand ExportCsvCommand { get; }

        private ComparisonResult? _lastResult;

        public MainViewModel()
        {
            DiscoverEnvironmentsCommand = new RelayCommand(async _ => await DiscoverEnvironmentsAsync());
            CompareEnvironmentsCommand = new RelayCommand(async _ => await CompareEnvironmentsAsync());
            ExportHtmlCommand = new RelayCommand(_ => ExportHtml());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv());

            // Initialize default mock environments for quick discovery
            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-dev", Url = "https://contoso-dev.crm.dynamics.com", IsSelected = true });
            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-test", Url = "https://contoso-test.crm.dynamics.com", IsSelected = true });
            DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-prod", Url = "https://contoso-prod.crm.dynamics.com", IsSelected = true });
        }

        public async Task DiscoverEnvironmentsAsync()
        {
            StatusMessage = "Discovering Dataverse environments via Global Discovery Service (OAuth)...";
            await Task.Delay(400); // UI responsive feel

            if (DiscoveredEnvironments.Count == 0)
            {
                DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-dev", Url = "https://contoso-dev.crm.dynamics.com", IsSelected = true });
                DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-test", Url = "https://contoso-test.crm.dynamics.com", IsSelected = true });
                DiscoveredEnvironments.Add(new SelectableEnv { Name = "contoso-prod", Url = "https://contoso-prod.crm.dynamics.com", IsSelected = true });
            }

            StatusMessage = $"Discovered {DiscoveredEnvironments.Count} environments. Select target scopes.";
        }

        public async Task CompareEnvironmentsAsync()
        {
            var selectedEnvs = DiscoveredEnvironments.Where(e => e.IsSelected).ToList();
            if (selectedEnvs.Count < 2)
            {
                StatusMessage = "Please select at least 2 environments to compare.";
                return;
            }

            StatusMessage = $"Comparing {selectedEnvs.Count} environments via D365 Web API / OAuth...";
            AdminSettingsTree.Clear();
            MetadataTree.Clear();

            var profiles = selectedEnvs.Select(e => new ConnectionProfile
            {
                EnvironmentUrl = e.Url,
                UseInteractiveAuth = true
            }).ToList();

            Scope.TargetEnvironments = profiles;

            var crawler = new EnvironmentMetadataCrawler(useSimulationMode: IsSimulationMode);
            var envDataList = new List<RawEnvData>();

            var progress = new Progress<ProgressUpdate>(p => {
                StatusMessage = $"[{p.Stage}] {p.Message}";
            });

            foreach (var profile in profiles)
            {
                var data = await crawler.CrawlEnvironmentAsync(profile, Scope, progress);
                envDataList.Add(data);
            }

            var comparer = new NWayComparer();
            _lastResult = comparer.CompareEnvironments(envDataList, Scope);

            foreach (var n in _lastResult.AdminSettingsNodes) AdminSettingsTree.Add(n);
            foreach (var n in _lastResult.MetadataNodes) MetadataTree.Add(n);

            TotalItems = _lastResult.TotalCount;
            IdenticalCount = _lastResult.IdenticalCount;
            DeltaCount = _lastResult.DeltaCount;
            UniqueCount = _lastResult.UniqueCount;

            StatusMessage = $"Comparison complete. Found {_lastResult.DeltaCount} Deltas, {_lastResult.UniqueCount} Unique items across {selectedEnvs.Count} environments.";
        }

        private void ExportHtml()
        {
            if (_lastResult == null) { StatusMessage = "No comparison results to export. Run a comparison first."; return; }
            string path = "environment_comparison_report.html";
            var exporter = new ComparatorExporter();
            exporter.ExportToHtml(path, _lastResult);
            StatusMessage = $"HTML Diff Dashboard exported to: {System.IO.Path.GetFullPath(path)}";
        }

        private void ExportCsv()
        {
            if (_lastResult == null) { StatusMessage = "No comparison results to export. Run a comparison first."; return; }
            string path = "environment_comparison_report.csv";
            var exporter = new ComparatorExporter();
            exporter.ExportToCsvExcel(path, _lastResult);
            StatusMessage = $"Excel/CSV Matrix exported to: {System.IO.Path.GetFullPath(path)}";
        }

        private void UpdateSelectedNodeProperties()
        {
            SelectedNodeProperties.Clear();
            if (SelectedNode?.PropertyDiffs != null)
            {
                foreach (var p in SelectedNode.PropertyDiffs)
                    SelectedNodeProperties.Add(p);
            }
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
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged;
    }
}
