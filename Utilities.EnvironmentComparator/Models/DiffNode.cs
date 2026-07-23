using System;
using System.Collections.Generic;

namespace Utilities.EnvironmentComparator.Models
{
    public enum DiffStatus
    {
        Identical,
        Delta,
        Unique
    }

    public enum RootCategory
    {
        AdminSettings,
        MetadataCustomizations
    }

    public class PropertyDiff
    {
        public string PropertyName { get; set; } = string.Empty;
        public Dictionary<string, string> ValuesPerEnv { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsMismatch { get; set; }
    }

    public class DiffNode
    {
        public RootCategory RootCategory { get; set; }
        public string SubCategory { get; set; } = string.Empty; // e.g., "OrgDbSettings", "PluginAssembly", "PluginStep", "TableColumn", "EnvVariable"
        public string UniqueKey { get; set; } = string.Empty; // e.g., "account.new_code"
        public string DisplayName { get; set; } = string.Empty;
        public string ParentComponent { get; set; } = string.Empty;
        public Dictionary<string, string> EnvironmentValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DiffStatus Status { get; set; } = DiffStatus.Identical;
        public List<PropertyDiff> PropertyDiffs { get; set; } = new();
        public List<DiffNode> Children { get; set; } = new();
    }
}
