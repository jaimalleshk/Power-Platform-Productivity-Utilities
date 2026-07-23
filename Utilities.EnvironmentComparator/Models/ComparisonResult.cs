using System;
using System.Collections.Generic;
using System.Linq;

namespace Utilities.EnvironmentComparator.Models
{
    public class ComparisonResult
    {
        public List<string> TargetEnvironmentNames { get; set; } = new();
        public DateTime ComparedAt { get; set; } = DateTime.UtcNow;
        public List<DiffNode> AdminSettingsNodes { get; set; } = new();
        public List<DiffNode> MetadataNodes { get; set; } = new();

        public int TotalCount => CountLeafNodes(AdminSettingsNodes) + CountLeafNodes(MetadataNodes);
        public int IdenticalCount { get; set; }
        public int DeltaCount { get; set; }
        public int UniqueCount { get; set; }

        private static int CountLeafNodes(IEnumerable<DiffNode> nodes)
        {
            if (nodes == null) return 0;
            int count = 0;
            foreach (var node in nodes)
            {
                if (node.Children == null || node.Children.Count == 0)
                {
                    count++;
                }
                else
                {
                    count += CountLeafNodes(node.Children);
                }
            }
            return count;
        }
    }
}
