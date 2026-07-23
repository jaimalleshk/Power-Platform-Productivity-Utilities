using System;
using System.Collections.Generic;
using System.IO;
using Utilities.EnvironmentComparator.Engine;
using Utilities.EnvironmentComparator.Models;
using Xunit;

namespace Core.Resilience.Tests
{
    public class EnvironmentComparatorTests
    {
        [Fact]
        public void NWayComparer_CalculatesDeltasAndUniquesCorrectly()
        {
            // Arrange
            var dev = new RawEnvData
            {
                EnvironmentName = "dev",
                AdminSettings = new() { ["OrgDbSettings.SkipRuleCheck"] = new() { ["Value"] = "True" } },
                MetadataItems = new()
                {
                    ["PluginAssembly.AccountPlugin.dll"] = new() { ["Version"] = "1.2.0.0" },
                    ["TableColumn.account.new_code"] = new() { ["MaxLength"] = "100" },
                    ["TableColumn.account.new_loyalty"] = new() { ["Type"] = "OptionSet" } // Unique in dev
                }
            };

            var prod = new RawEnvData
            {
                EnvironmentName = "prod",
                AdminSettings = new() { ["OrgDbSettings.SkipRuleCheck"] = new() { ["Value"] = "True" } },
                MetadataItems = new()
                {
                    ["PluginAssembly.AccountPlugin.dll"] = new() { ["Version"] = "1.1.0.0" }, // Delta version
                    ["TableColumn.account.new_code"] = new() { ["MaxLength"] = "50" } // Delta length
                }
            };

            var comparer = new NWayComparer();
            var scope = new ComparisonScope();

            // Act
            var result = comparer.CompareEnvironments(new List<RawEnvData> { dev, prod }, scope);

            // Assert
            Assert.Equal(2, result.TargetEnvironmentNames.Count);
            Assert.Equal(4, result.TotalCount);
            Assert.True(result.DeltaCount >= 2, "Expected at least 2 deltas (Plugin version mismatch, MaxLength mismatch)");
            Assert.True(result.UniqueCount >= 1, "Expected at least 1 unique item (new_loyalty column)");
        }

        [Fact]
        public void ComparatorExporter_ExportsHtmlAndCsvSuccessfully()
        {
            // Arrange
            string tempHtml = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.html");
            string tempCsv = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");

            var result = new ComparisonResult
            {
                TargetEnvironmentNames = new List<string> { "dev", "prod" },
                AdminSettingsNodes = new List<DiffNode>
                {
                    new DiffNode
                    {
                        RootCategory = RootCategory.AdminSettings,
                        SubCategory = "OrgDbSettings",
                        UniqueKey = "OrgDbSettings.SkipRuleCheck",
                        DisplayName = "SkipRuleCheck",
                        Status = DiffStatus.Identical,
                        EnvironmentValues = new() { ["dev"] = "True", ["prod"] = "True" }
                    }
                },
                MetadataNodes = new List<DiffNode>
                {
                    new DiffNode
                    {
                        RootCategory = RootCategory.MetadataCustomizations,
                        SubCategory = "PluginAssembly",
                        UniqueKey = "PluginAssembly.AccountPlugin.dll",
                        DisplayName = "AccountPlugin.dll",
                        Status = DiffStatus.Delta,
                        EnvironmentValues = new() { ["dev"] = "v1.2.0", ["prod"] = "v1.1.0" }
                    }
                }
            };

            var exporter = new ComparatorExporter();

            try
            {
                // Act
                exporter.ExportToHtml(tempHtml, result);
                exporter.ExportToCsvExcel(tempCsv, result);

                // Assert
                Assert.True(File.Exists(tempHtml));
                Assert.True(File.Exists(tempCsv));

                string htmlText = File.ReadAllText(tempHtml);
                string csvText = File.ReadAllText(tempCsv);

                Assert.Contains("AccountPlugin.dll", htmlText);
                Assert.Contains("AccountPlugin.dll", csvText);
            }
            finally
            {
                if (File.Exists(tempHtml)) File.Delete(tempHtml);
                if (File.Exists(tempCsv)) File.Delete(tempCsv);
            }
        }
    }
}
