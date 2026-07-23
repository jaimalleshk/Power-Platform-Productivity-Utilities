using System;
using System.Collections.Generic;

namespace Utilities.EnvironmentComparator.Models
{
    public class ComparisonResult
    {
        public List<string> TargetEnvironmentNames { get; set; } = new();
        public DateTime ComparedAt { get; set; } = DateTime.UtcNow;
        public List<DiffNode> AdminSettingsNodes { get; set; } = new();
        public List<DiffNode> MetadataNodes { get; set; } = new();

        public int TotalCount => AdminSettingsNodes.Count + MetadataNodes.Count;
        public int IdenticalCount { get; set; }
        public int DeltaCount { get; set; }
        public int UniqueCount { get; set; }
    }
}
