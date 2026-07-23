using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine
{
    public class NWayComparer
    {
        public ComparisonResult CompareEnvironments(List<RawEnvData> envDataList, ComparisonScope scope)
        {
            var result = new ComparisonResult
            {
                TargetEnvironmentNames = envDataList.Select(e => e.EnvironmentName).ToList(),
                ComparedAt = DateTime.UtcNow
            };

            // 1. Process Admin Settings (Root 1)
            var adminKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var env in envDataList)
            {
                foreach (var k in env.AdminSettings.Keys) adminKeys.Add(k);
            }

            foreach (var key in adminKeys.OrderBy(k => k))
            {
                var node = BuildNode(key, RootCategory.AdminSettings, envDataList, (env) => 
                    env.AdminSettings.TryGetValue(key, out var dict) ? dict : null);

                if (MatchesFilter(node, scope))
                {
                    result.AdminSettingsNodes.Add(node);
                    IncrementStats(result, node.Status);
                }
            }

            // 2. Process Metadata & Customizations (Root 2)
            var metadataKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var env in envDataList)
            {
                foreach (var k in env.MetadataItems.Keys) metadataKeys.Add(k);
            }

            foreach (var key in metadataKeys.OrderBy(k => k))
            {
                var node = BuildNode(key, RootCategory.MetadataCustomizations, envDataList, (env) => 
                    env.MetadataItems.TryGetValue(key, out var dict) ? dict : null);

                if (MatchesFilter(node, scope))
                {
                    result.MetadataNodes.Add(node);
                    IncrementStats(result, node.Status);
                }
            }

            return result;
        }

        private DiffNode BuildNode(
            string fullKey, 
            RootCategory rootCategory, 
            List<RawEnvData> envDataList, 
            Func<RawEnvData, Dictionary<string, string>?> dictSelector)
        {
            string subCategory = fullKey.Contains('.') ? fullKey.Split('.')[0] : "General";
            string displayName = fullKey.Contains('.') ? fullKey.Substring(fullKey.IndexOf('.') + 1) : fullKey;

            var node = new DiffNode
            {
                RootCategory = rootCategory,
                SubCategory = subCategory,
                UniqueKey = fullKey,
                DisplayName = displayName
            };

            int presentCount = 0;
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var envSummaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var env in envDataList)
            {
                var props = dictSelector(env);
                if (props != null && props.Count > 0)
                {
                    presentCount++;
                    // Primary summary value is the first property or 'Value' property
                    string summaryVal = props.TryGetValue("Value", out var v) ? v : 
                        (props.TryGetValue("Version", out var ver) ? ver : 
                        (props.TryGetValue("Status", out var st) ? st : 
                        (props.TryGetValue("MaxLength", out var ml) ? $"MaxLength={ml}" : "Present")));

                    envSummaries[env.EnvironmentName] = summaryVal;

                    foreach (var pKey in props.Keys) propertyNames.Add(pKey);
                }
                else
                {
                    envSummaries[env.EnvironmentName] = "Missing 🔷";
                }
            }

            node.EnvironmentValues = envSummaries;

            // Classify Diff Status
            if (presentCount < envDataList.Count)
            {
                node.Status = DiffStatus.Unique;
            }
            else
            {
                // Check if all summary values match across environments
                string firstVal = envSummaries.Values.First();
                bool allMatch = envSummaries.Values.All(val => string.Equals(val, firstVal, StringComparison.OrdinalIgnoreCase));
                
                node.Status = allMatch ? DiffStatus.Identical : DiffStatus.Delta;
            }

            // Build Property-Level Diffs
            foreach (var propName in propertyNames.OrderBy(p => p))
            {
                var pDiff = new PropertyDiff { PropertyName = propName };
                var vals = new List<string>();

                foreach (var env in envDataList)
                {
                    var props = dictSelector(env);
                    string valStr = (props != null && props.TryGetValue(propName, out var pVal)) ? pVal : "[N/A]";
                    pDiff.ValuesPerEnv[env.EnvironmentName] = valStr;
                    vals.Add(valStr);
                }

                pDiff.IsMismatch = vals.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                node.PropertyDiffs.Add(pDiff);

                if (pDiff.IsMismatch && node.Status == DiffStatus.Identical)
                {
                    node.Status = DiffStatus.Delta; // Upgrade status if property mismatch detected
                }
            }

            return node;
        }

        private bool MatchesFilter(DiffNode node, ComparisonScope scope)
        {
            if (!string.IsNullOrEmpty(scope.SearchFilter))
            {
                bool keyMatch = node.UniqueKey.Contains(scope.SearchFilter, StringComparison.OrdinalIgnoreCase);
                bool nameMatch = node.DisplayName.Contains(scope.SearchFilter, StringComparison.OrdinalIgnoreCase);
                if (!keyMatch && !nameMatch) return false;
            }

            return true;
        }

        private void IncrementStats(ComparisonResult result, DiffStatus status)
        {
            switch (status)
            {
                case DiffStatus.Identical: result.IdenticalCount++; break;
                case DiffStatus.Delta: result.DeltaCount++; break;
                case DiffStatus.Unique: result.UniqueCount++; break;
            }
        }
    }
}
