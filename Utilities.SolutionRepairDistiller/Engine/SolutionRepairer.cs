using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Connections;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.SolutionRepairDistiller.Models;

namespace Utilities.SolutionRepairDistiller.Engine
{
    public class SolutionRepairer
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly bool _useSimulationMode;

        private static readonly JsonSerializerOptions PascalCaseJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null
        };

        public SolutionRepairer(IConnectionFactory? connectionFactory = null, bool useSimulationMode = false)
        {
            _connectionFactory = connectionFactory ?? new DataverseConnectionFactory();
            _useSimulationMode = useSimulationMode;
        }

        public async Task<int> RepairSolutionAsync(
            string reportJsonPath,
            ConnectionProfile targetProfile,
            ConnectionProfile? sourceProfile = null,
            string? solutionName = null,
            IProgress<ProgressUpdate>? progress = null)
        {
            progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"Reading validation report from: {reportJsonPath}...", PercentComplete = 5 });

            if (!File.Exists(reportJsonPath))
            {
                throw new FileNotFoundException("Validation report JSON not found.", reportJsonPath);
            }

            // Parse JSON report
            ValidationReport? report;
            try
            {
                string jsonText = await File.ReadAllTextAsync(reportJsonPath).ConfigureAwait(false);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                report = JsonSerializer.Deserialize<ValidationReport>(jsonText, options);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse validation report JSON: {ex.Message}", ex);
            }

            if (report == null || report.Issues == null || report.Issues.Count == 0)
            {
                progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = "No issues detected in report. Nothing to repair.", PercentComplete = 100 });
                return 0;
            }

            int resolvedCount = 0;
            int totalIssues = report.Issues.Count;
            double progressStep = 90.0 / totalIssues;
            double currentPercent = 10.0;

            progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"Found {totalIssues} issues to evaluate. Connecting to target environment...", PercentComplete = 10 });

            using var targetClient = _useSimulationMode ? null : _connectionFactory.CreateHttpClient(targetProfile);
            using var sourceClient = (_useSimulationMode || sourceProfile == null) ? null : _connectionFactory.CreateHttpClient(sourceProfile);

            foreach (var issue in report.Issues)
            {
                currentPercent += progressStep;
                
                // Repair A: Active unmanaged layers on target (WRN-B1)
                if (issue.Id.Equals("WRN-B1", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new ProgressUpdate 
                    { 
                        Stage = "Solution Repairer", 
                        Message = $"Repairing active unmanaged layer on '{issue.LogicalName}' ({issue.ComponentType}) on target...", 
                        PercentComplete = currentPercent 
                    });

                    bool success = await RemoveUnmanagedLayerAsync(targetClient, issue, progress).ConfigureAwait(false);
                    if (success) resolvedCount++;
                }
                // Repair B: Missing dependency (MISSING_DEPENDENCY / UNMANAGED_DEPENDENCY)
                else if ((issue.Id.Equals("MISSING_DEPENDENCY", StringComparison.OrdinalIgnoreCase) || 
                          issue.Id.Equals("UNMANAGED_DEPENDENCY", StringComparison.OrdinalIgnoreCase) ||
                          issue.Id.Equals("INTERNAL_UNMANAGED", StringComparison.OrdinalIgnoreCase)) 
                         && sourceProfile != null && !string.IsNullOrEmpty(solutionName))
                {
                    progress?.Report(new ProgressUpdate 
                    { 
                        Stage = "Solution Repairer", 
                        Message = $"Repairing missing dependency for '{issue.LogicalName}' by adding to source solution '{solutionName}'...", 
                        PercentComplete = currentPercent 
                    });

                    bool success = await AddMissingDependencyToSourceAsync(sourceClient, solutionName, issue, progress).ConfigureAwait(false);
                    if (success) resolvedCount++;
                }
                else
                {
                    progress?.Report(new ProgressUpdate 
                    { 
                        Stage = "Solution Repairer", 
                        Message = $"Skipping unresolvable issue '{issue.Id}' on '{issue.LogicalName}'. Only unmanaged active layers and source-side missing dependencies can be repaired automatically.", 
                        PercentComplete = currentPercent 
                    });
                }
            }

            progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"Repair complete. Programmatically resolved {resolvedCount} of {totalIssues} issues.", PercentComplete = 100 });
            return resolvedCount;
        }

        private async Task<bool> RemoveUnmanagedLayerAsync(
            HttpClient? targetClient, 
            ValidationIssue issue, 
            IProgress<ProgressUpdate>? progress)
        {
            string entitySetName = MapComponentTypeToEntitySet(issue.ComponentType);
            if (string.IsNullOrEmpty(entitySetName))
            {
                progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"[Warning] Component type '{issue.ComponentType}' not supported for automatic active layer removal.", Status = ProgressStatus.Warning });
                return false;
            }

            // We need the Component ID (GUID) to call the bound action.
            // If the validation log did not record the GUID, we might try to extract it from help OData queries or other logical properties.
            // Let's search if the description contains a GUID.
            string guid = ExtractGuid(issue.Description) ?? ExtractGuid(issue.HelpODataQuery) ?? "";
            
            if (string.IsNullOrEmpty(guid))
            {
                // Fallback simulation or warning
                if (_useSimulationMode)
                {
                    guid = Guid.NewGuid().ToString("D");
                }
                else
                {
                    progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"[Warning] Could not resolve unique ID (GUID) for component '{issue.LogicalName}'. Skipping layer removal.", Status = ProgressStatus.Warning });
                    return false;
                }
            }

            if (_useSimulationMode)
            {
                await Task.Delay(300).ConfigureAwait(false);
                progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"(Simulation) POST {entitySetName}({guid})/Microsoft.Dynamics.CRM.RemoveActiveCustomizations", PercentComplete = -1 });
                return true;
            }

            try
            {
                string actionUrl = $"{entitySetName}({guid})/Microsoft.Dynamics.CRM.RemoveActiveCustomizations";
                var res = await targetClient!.PostAsJsonAsync(actionUrl, new { }, PascalCaseJsonOptions).ConfigureAwait(false);
                
                if (res.IsSuccessStatusCode)
                {
                    progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"Successfully removed unmanaged active layer for component '{issue.LogicalName}'.", Status = ProgressStatus.Info });
                    return true;
                }
                else
                {
                    string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"[Error] Failed to remove active layer for '{issue.LogicalName}': {err}", Status = ProgressStatus.Error });
                }
            }
            catch (Exception ex)
            {
                progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"[Error] Exception during active layer removal for '{issue.LogicalName}': {ex.Message}", Status = ProgressStatus.Error });
            }

            return false;
        }

        private async Task<bool> AddMissingDependencyToSourceAsync(
            HttpClient? sourceClient,
            string solutionName,
            ValidationIssue issue,
            IProgress<ProgressUpdate>? progress)
        {
            // We need the dependency ID and type code.
            // Let's see: we extract a GUID if present in the issue details.
            string guid = ExtractGuid(issue.LogicalName) ?? ExtractGuid(issue.Description) ?? "";
            int typeCode = MapComponentTypeNameToCode(issue.ComponentType);

            if (string.IsNullOrEmpty(guid) || typeCode == 0)
            {
                if (_useSimulationMode)
                {
                    guid = Guid.NewGuid().ToString("D");
                    typeCode = 60; // SystemForm
                }
                else
                {
                    progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"[Warning] Could not resolve ComponentId GUID or TypeCode for missing dependency '{issue.LogicalName}'. Skipping add-to-source.", Status = ProgressStatus.Warning });
                    return false;
                }
            }

            var addPayload = new
            {
                ComponentId = guid,
                ComponentType = typeCode,
                SolutionUniqueName = solutionName,
                AddRequiredComponents = false,
                DoNotIncludeSubcomponents = true
            };

            if (_useSimulationMode)
            {
                await Task.Delay(300).ConfigureAwait(false);
                progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"(Simulation) POST AddSolutionComponent -> Adding dependency '{issue.LogicalName}' (Type {typeCode}) to source solution '{solutionName}'...", PercentComplete = -1 });
                return true;
            }

            try
            {
                var res = await sourceClient!.PostAsJsonAsync("AddSolutionComponent", addPayload, PascalCaseJsonOptions).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"Successfully added dependency '{issue.LogicalName}' (Type {typeCode}) to source solution '{solutionName}'.", Status = ProgressStatus.Info });
                    return true;
                }
                else
                {
                    string err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"[Error] Failed to add dependency '{issue.LogicalName}' to source solution: {err}", Status = ProgressStatus.Error });
                }
            }
            catch (Exception ex)
            {
                progress?.Report(new ProgressUpdate { Stage = "Solution Repairer", Message = $"[Error] Exception adding dependency to source solution: {ex.Message}", Status = ProgressStatus.Error });
            }

            return false;
        }

        private static string MapComponentTypeToEntitySet(string typeName)
        {
            typeName = typeName.ToLowerInvariant().Trim();
            return typeName switch
            {
                "systemform" => "systemforms",
                "workflow" => "workflows",
                "webresource" => "webresources",
                "role" => "roles",
                _ => ""
            };
        }

        private static int MapComponentTypeNameToCode(string typeName)
        {
            typeName = typeName.ToLowerInvariant().Trim();
            return typeName switch
            {
                "entity" => 1,
                "attribute" => 2,
                "relationship" => 3,
                "optionset" => 9,
                "role" => 20,
                "workflow" => 29,
                "systemform" => 60,
                "webresource" => 61,
                "sitemap" => 62,
                "ribboncustomization" => 63,
                "pluginassembly" => 90,
                "pluginstep" => 92,
                "connectionreference" => 10047,
                "appaction" => 10243,
                _ => 0
            };
        }

        private static string? ExtractGuid(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            
            // Match standard GUID regex: 8-4-4-4-12
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\b[A-Fa-f0-9]{8}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{12}\b");
            return match.Success ? match.Value : null;
        }
    }
}
