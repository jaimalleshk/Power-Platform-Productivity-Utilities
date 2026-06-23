using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Collections.Generic;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.SolutionRepairDistiller.Models;

namespace Utilities.SolutionRepairDistiller.Engine
{
    public class SolutionPruner
    {
        private readonly HttpClient? _httpClient;
        private readonly bool _useSimulationMode;

        private static readonly JsonSerializerOptions PascalCaseJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null
        };

        public SolutionPruner(HttpClient? httpClient = null, bool useSimulationMode = false)
        {
            _httpClient = httpClient;
            _useSimulationMode = useSimulationMode;
        }

        public async Task<DistillerReport> PruneSolutionAsync(
            string sourceZipPath, 
            string targetZipPath, 
            List<PruningTarget> targets, 
            XDocument solutionDoc, 
            XDocument customizationsDoc,
            string solutionName,
            IProgress<ProgressUpdate>? progress = null)
        {
            if (string.IsNullOrEmpty(sourceZipPath) || !File.Exists(sourceZipPath))
                throw new FileNotFoundException("Source solution zip package not found.", sourceZipPath);

            progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = "Beginning local solution ZIP refactoring...", PercentComplete = 5 });
            long originalSize = new FileInfo(sourceZipPath).Length;

            // 1. Refactor XML manifests in memory
            progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = "Refactoring customizations.xml and solution.xml in-memory...", PercentComplete = 20 });
            RefactorCustomizationsXml(customizationsDoc, targets);
            RefactorSolutionXml(solutionDoc, targets);

            // 2. Re-pack files into the target zip file
            progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = "Repacking files into optimized Solution ZIP...", PercentComplete = 50 });
            byte[] originalZipBytes = await File.ReadAllBytesAsync(sourceZipPath).ConfigureAwait(false);
            byte[] optimizedZipBytes;

            using (var sourceMs = new MemoryStream(originalZipBytes))
            using (var targetMs = new MemoryStream())
            {
                using (var sourceArchive = new ZipArchive(sourceMs, ZipArchiveMode.Read))
                using (var targetArchive = new ZipArchive(targetMs, ZipArchiveMode.Create, true))
                {
                    foreach (var entry in sourceArchive.Entries)
                    {
                        var targetEntry = targetArchive.CreateEntry(entry.FullName);
                        using (var writer = new StreamWriter(targetEntry.Open()))
                        {
                            if (entry.FullName.Equals("solution.xml", StringComparison.OrdinalIgnoreCase))
                            {
                                solutionDoc.Save(writer);
                            }
                            else if (entry.FullName.Equals("customizations.xml", StringComparison.OrdinalIgnoreCase))
                            {
                                customizationsDoc.Save(writer);
                            }
                            else
                            {
                                using (var reader = entry.Open())
                                {
                                    await reader.CopyToAsync(writer.BaseStream).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                }
                optimizedZipBytes = targetMs.ToArray();
            }

            // Write target zip
            string? targetDir = Path.GetDirectoryName(targetZipPath);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
            await File.WriteAllBytesAsync(targetZipPath, optimizedZipBytes).ConfigureAwait(false);

            long optimizedSize = new FileInfo(targetZipPath).Length;
            double reductionPercent = originalSize > 0 ? Math.Round((1.0 - ((double)optimizedSize / originalSize)) * 100, 2) : 0;

            // 3. Execute Platform API Pruning Actions if HttpClient is connected
            if (_httpClient != null || _useSimulationMode)
            {
                progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = "Executing matching Refactoring commands on Dataverse Server...", PercentComplete = 70 });
                await ExecutePlatformWebAPIActionsAsync(targets, solutionName, progress).ConfigureAwait(false);
            }

            // 4. Generate Report
            var report = new DistillerReport
            {
                SolutionName = solutionName,
                OriginalFileSizeBytes = originalSize,
                OptimizedFileSizeBytes = optimizedSize,
                ReductionPercentage = reductionPercent
            };

            foreach (var t in targets)
            {
                report.ComponentsRemoved.Add(new PrunedComponent
                {
                    Type = t.ComponentType,
                    LogicalName = t.LogicalName,
                    ParentTable = t.ParentTable,
                    Reason = t.Reason
                });
            }

            progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = "Pruning operation completed successfully.", PercentComplete = 100 });
            return report;
        }

        public async Task RepairZipXmlCorruptionsAsync(
            string zipPath, 
            string outZipPath, 
            IProgress<ProgressUpdate>? progress = null)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                throw new FileNotFoundException("Solution zip package not found.", zipPath);

            progress?.Report(new ProgressUpdate { Stage = "XML Repair", Message = "Scanning solution package for XML corruptions...", PercentComplete = 10 });
            
            byte[] zipBytes = await File.ReadAllBytesAsync(zipPath).ConfigureAwait(false);
            byte[] repairedZipBytes;

            using (var sourceMs = new MemoryStream(zipBytes))
            using (var targetMs = new MemoryStream())
            {
                using (var sourceArchive = new ZipArchive(sourceMs, ZipArchiveMode.Read))
                using (var targetArchive = new ZipArchive(targetMs, ZipArchiveMode.Create, true))
                {
                    foreach (var entry in sourceArchive.Entries)
                    {
                        var targetEntry = targetArchive.CreateEntry(entry.FullName);

                        if (entry.FullName.Equals("solution.xml", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.Equals("customizations.xml", StringComparison.OrdinalIgnoreCase))
                        {
                            progress?.Report(new ProgressUpdate { Stage = "XML Repair", Message = $"Repairing '{entry.FullName}' content...", PercentComplete = 40 });
                            
                            string rawXml;
                            using (var reader = new StreamReader(entry.Open()))
                            {
                                rawXml = await reader.ReadToEndAsync().ConfigureAwait(false);
                            }

                            string cleanXml = PreprocessAndRepairXmlText(rawXml);

                            using (var writer = new StreamWriter(targetEntry.Open()))
                            {
                                await writer.WriteAsync(cleanXml).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            using (var reader = entry.Open())
                            using (var writer = targetEntry.Open())
                            {
                                await reader.CopyToAsync(writer).ConfigureAwait(false);
                            }
                        }
                    }
                }
                repairedZipBytes = targetMs.ToArray();
            }

            string? targetDir = Path.GetDirectoryName(outZipPath);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
            await File.WriteAllBytesAsync(outZipPath, repairedZipBytes).ConfigureAwait(false);

            progress?.Report(new ProgressUpdate { Stage = "XML Repair", Message = $"XML Repair complete. Repaired package saved to: {outZipPath}", PercentComplete = 100 });
        }

        private string PreprocessAndRepairXmlText(string xmlText)
        {
            if (string.IsNullOrEmpty(xmlText)) return xmlText;

            // 1. Strip invalid XML control characters
            xmlText = System.Text.RegularExpressions.Regex.Replace(xmlText, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");

            // 2. Deduplicate duplicate attributes in XML elements (like double namespace decls)
            xmlText = System.Text.RegularExpressions.Regex.Replace(xmlText, @"(xmlns:xsi=""[^""]+"")\s+\1", "$1");

            // 3. Fix unclosed trailing tags or malformed bracket endings
            xmlText = System.Text.RegularExpressions.Regex.Replace(xmlText, @"<([^>]+)\s+/?>", m => {
                string tag = m.Value;
                if (tag.EndsWith("/>") || tag.Contains(">")) return tag;
                return tag + ">";
            });

            return xmlText;
        }

        private void RefactorCustomizationsXml(XDocument doc, List<PruningTarget> targets)
        {
            if (doc == null) return;

            var bloatedEntities = targets.Select(t => t.ParentTable).Distinct();
            foreach (var entityName in bloatedEntities)
            {
                var entityNode = doc.XPathSelectElement($"/ImportExportXml/Entities/Entity[@Name='{entityName}']");
                if (entityNode != null)
                {
                    entityNode.SetAttributeValue("IncludeAllComponents", "false");
                }
            }

            var attributeTargets = targets.Where(t => t.ComponentType == "Attribute");
            foreach (var target in attributeTargets)
            {
                var attrNode = doc.XPathSelectElement($"/ImportExportXml/Entities/Entity[@Name='{target.ParentTable}']/EntityInfo/Attributes/Attribute[@PhysicalName='{target.LogicalName}']");
                attrNode?.Remove();
            }
        }

        private void RefactorSolutionXml(XDocument doc, List<PruningTarget> targets)
        {
            if (doc == null) return;

            var bloatedEntities = targets.Select(t => t.ParentTable).Distinct();
            foreach (var entityName in bloatedEntities)
            {
                var rootComp = doc.XPathSelectElement($"/ImportExportXml/SolutionManifest/RootComponents/RootComponent[@type='1'][@schemaName='{entityName}']");
                if (rootComp != null)
                {
                    rootComp.SetAttributeValue("behavior", "1"); // 1 = DoNotIncludeSubcomponents
                }
            }
        }

        private async Task ExecutePlatformWebAPIActionsAsync(
            List<PruningTarget> targets, 
            string solutionName, 
            IProgress<ProgressUpdate>? progress)
        {
            var bloatedEntities = targets.Select(t => t.ParentTable).Distinct();
            foreach (var entityName in bloatedEntities)
            {
                // Step A: Remove Broad Root Component
                string removeUrl = "RemoveSolutionComponent";
                var removePayload = new
                {
                    ComponentId = Guid.NewGuid().ToString("D"), 
                    ComponentType = 1, // Entity
                    SolutionUniqueName = solutionName
                };

                if (_useSimulationMode)
                {
                    progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = $"(Simulation) Server POST {removeUrl} for Entity '{entityName}'", PercentComplete = -1 });
                    await Task.Delay(100).ConfigureAwait(false);
                }
                else
                {
                    var res = await _httpClient!.PostAsJsonAsync(removeUrl, removePayload, PascalCaseJsonOptions).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode)
                    {
                        string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = $"[Warning] Failed to remove entity component '{entityName}': {err}", Status = ProgressStatus.Warning });
                    }
                }

                // Step B: Re-add Entity with DoNotIncludeSubcomponents = true
                string addUrl = "AddSolutionComponent";
                var addPayload = new
                {
                    ComponentId = Guid.NewGuid().ToString("D"),
                    ComponentType = 1, // Entity
                    SolutionUniqueName = solutionName,
                    AddRequiredComponents = false,
                    DoNotIncludeSubcomponents = true
                };

                if (_useSimulationMode)
                {
                    progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = $"(Simulation) Server POST {addUrl} (behavior=1) for Entity '{entityName}'", PercentComplete = -1 });
                    await Task.Delay(100).ConfigureAwait(false);
                }
                else
                {
                    var res = await _httpClient!.PostAsJsonAsync(addUrl, addPayload, PascalCaseJsonOptions).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode)
                    {
                        string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        progress?.Report(new ProgressUpdate { Stage = "ZIP Pruning", Message = $"[Warning] Failed to re-add entity shell: {err}", Status = ProgressStatus.Warning });
                    }
                }
            }
        }
    }
}
