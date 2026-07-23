using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Utilities.EnvironmentComparator.Engine;
using Utilities.EnvironmentComparator.Models;
using Utilities.EnvironmentComparator.Storage;
using Xunit;

namespace Core.Resilience.Tests
{
    public class OfflineStorageTests
    {
        [Fact]
        public void OfflineStorageEngine_SavesAndLoadsSnapshotsInSqlite()
        {
            // Arrange
            string tempDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sqlite");
            var engine = new OfflineStorageEngine();

            var devData = new RawEnvData
            {
                EnvironmentName = "contoso-dev",
                AdminSettings = new() { ["OrgDbSettings.SkipRuleCheck"] = new() { ["Value"] = "True" } },
                MetadataItems = new()
                {
                    ["PluginAssembly.AccountPlugin.dll"] = new() { ["Version"] = "1.2.0.0" },
                    ["EntityForm.account.Information"] = new() { ["FormType"] = "Main (2)" }
                }
            };

            try
            {
                // Act
                engine.SaveSnapshot(tempDbPath, devData);
                var snapshots = engine.GetSnapshots(tempDbPath);
                var loadedDev = engine.LoadSnapshot(tempDbPath, "contoso-dev");

                // Assert
                Assert.True(File.Exists(tempDbPath));
                Assert.Single(snapshots);
                Assert.Equal("contoso-dev", snapshots[0].EnvironmentName);
                Assert.Single(loadedDev.AdminSettings);
                Assert.Equal("1.2.0.0", loadedDev.MetadataItems["PluginAssembly.AccountPlugin.dll"]["Version"]);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(tempDbPath))
                {
                    try { File.Delete(tempDbPath); } catch { }
                }
            }
        }

        [Fact]
        public void OfflineStorageEngine_RunsOfflineComparisonFromSqlite()
        {
            // Arrange
            string tempDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sqlite");
            var engine = new OfflineStorageEngine();

            var devData = new RawEnvData
            {
                EnvironmentName = "contoso-dev",
                AdminSettings = new() { ["OrgDbSettings.SkipRuleCheck"] = new() { ["Value"] = "True" } },
                MetadataItems = new() { ["PluginAssembly.AccountPlugin.dll"] = new() { ["Version"] = "1.2.0.0" } }
            };

            var prodData = new RawEnvData
            {
                EnvironmentName = "contoso-prod",
                AdminSettings = new() { ["OrgDbSettings.SkipRuleCheck"] = new() { ["Value"] = "True" } },
                MetadataItems = new() { ["PluginAssembly.AccountPlugin.dll"] = new() { ["Version"] = "1.1.0.0" } }
            };

            try
            {
                // Act
                engine.SaveSnapshot(tempDbPath, devData);
                engine.SaveSnapshot(tempDbPath, prodData);

                var scope = new ComparisonScope();
                var result = engine.CompareSnapshotsOffline(tempDbPath, new List<string> { "contoso-dev", "contoso-prod" }, scope);

                // Assert
                Assert.Equal(2, result.TargetEnvironmentNames.Count);
                Assert.Equal(2, result.TotalCount);
                Assert.Equal(1, result.DeltaCount); // PluginAssembly version delta
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(tempDbPath))
                {
                    try { File.Delete(tempDbPath); } catch { }
                }
            }
        }

        [Fact]
        public void ExcelReportGenerator_ExportsFormattedXmlExcelFile()
        {
            // Arrange
            string tempExcelPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xml");
            var result = new ComparisonResult
            {
                TargetEnvironmentNames = new List<string> { "dev", "prod" },
                AdminSettingsNodes = new List<DiffNode>
                {
                    new DiffNode
                    {
                        SubCategory = "OrgDbSettings",
                        UniqueKey = "OrgDbSettings.SkipRuleCheck",
                        DisplayName = "SkipRuleCheck",
                        Status = DiffStatus.Identical,
                        EnvironmentValues = new() { ["dev"] = "True", ["prod"] = "True" }
                    }
                }
            };

            var generator = new ExcelReportGenerator();

            try
            {
                // Act
                generator.ExportFormattedExcelXml(tempExcelPath, result);

                // Assert
                Assert.True(File.Exists(tempExcelPath));
                string content = File.ReadAllText(tempExcelPath);
                Assert.Contains("urn:schemas-microsoft-com:office:spreadsheet", content);
                Assert.Contains("SkipRuleCheck", content);
                Assert.Contains("StatusIdentical", content);
            }
            finally
            {
                if (File.Exists(tempExcelPath))
                {
                    try { File.Delete(tempExcelPath); } catch { }
                }
            }
        }

        [Fact]
        public void TextDiffEngine_FormatsJavaScriptWithSyntaxHighlightingAndColors()
        {
            // Arrange
            var diffEngine = new TextDiffEngine();
            string oldJs = "function onSave(executionContext) {\n    var accountName = 'Contoso Dev';\n}";
            string newJs = "function onSave(executionContext) {\n    const accountName = 'Contoso Prod';\n    console.log('Saved');\n}";

            // Act
            string htmlDiff = diffEngine.GenerateHtmlColorDiffView(oldJs, newJs, "js");

            // Assert
            Assert.Contains("function", htmlDiff);
            Assert.Contains("569cd6", htmlDiff); // JS Keyword Blue Color
            Assert.Contains("Contoso Prod", htmlDiff);
            Assert.Contains("rgba(34, 197, 94", htmlDiff); // Added Line Green Highlight
        }
    }
}
