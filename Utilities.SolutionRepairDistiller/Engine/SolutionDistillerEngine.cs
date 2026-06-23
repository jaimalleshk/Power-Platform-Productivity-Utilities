using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Reporting;

namespace Utilities.SolutionRepairDistiller.Engine
{
    public class SolutionDistillerEngine
    {
        private readonly HttpClient? _httpClient;
        private readonly bool _useSimulationMode;

        private static readonly JsonSerializerOptions PascalCaseJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null
        };

        public SolutionDistillerEngine(HttpClient? httpClient = null, bool useSimulationMode = false)
        {
            _httpClient = httpClient;
            _useSimulationMode = useSimulationMode;
        }

        public async Task<DistillerReport> DistillSolutionAsync(
            string solutionName, 
            IProgress<ProgressUpdate>? progress = null)
        {
            var report = new DistillerReport
            {
                SolutionName = solutionName,
                OriginalFileSizeBytes = 0,
                OptimizedFileSizeBytes = 0,
                ReductionPercentage = 0
            };

            progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = $"Connecting to source environment and fetching solution '{solutionName}'...", PercentComplete = 10 });

            if (_useSimulationMode)
            {
                await Task.Delay(800).ConfigureAwait(false);
                progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = "(Simulation) Analyzing solution component hierarchy...", PercentComplete = 40 });
                await Task.Delay(500).ConfigureAwait(false);

                // Simulate pruning account entity
                report.ComponentsRemoved.Add(new PrunedComponent
                {
                    Type = "Entity",
                    LogicalName = "account",
                    ParentTable = "",
                    Reason = "OOB entity pruned to DoNotIncludeSubcomponents (behavior=1) directly on server."
                });

                progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = "(Simulation) Solution 'account' entity successfully distilled on server.", PercentComplete = 90 });
                await Task.Delay(300).ConfigureAwait(false);
                progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = "Distillation complete.", PercentComplete = 100 });
                return report;
            }

            if (_httpClient == null)
            {
                throw new InvalidOperationException("HttpClient must be provided to run server-based distillation.");
            }

            // 1. Fetch solution components
            string queryUrl = $"solutions?$filter=uniquename eq '{solutionName}'&$expand=solution_solutioncomponent($select=componenttype,objectid,rootcomponentbehavior)";
            var response = await _httpClient.GetAsync(queryUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"Failed to query solution components: {response.StatusCode}. Details: {err}");
            }

            using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
            if (doc == null) throw new InvalidOperationException("Empty response from solution query.");

            var root = doc.RootElement;
            if (!root.TryGetProperty("value", out var valueArray) || valueArray.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"Solution '{solutionName}' not found in the environment.");
            }

            var solutionRecord = valueArray[0];
            if (!solutionRecord.TryGetProperty("solution_solutioncomponent", out var componentsArray))
            {
                progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = "No components found in the solution. Nothing to distill.", PercentComplete = 100 });
                return report;
            }

            var bloatedEntities = new List<(string ObjectId, string LogicalName)>();
            int processedCount = 0;
            int totalComponents = componentsArray.GetArrayLength();

            progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = $"Found {totalComponents} solution components. Scanning for OOB entity bloat...", PercentComplete = 30 });

            foreach (var comp in componentsArray.EnumerateArray())
            {
                processedCount++;
                int componentType = comp.TryGetProperty("componenttype", out var typeProp) ? typeProp.GetInt32() : 0;
                string objectId = comp.TryGetProperty("objectid", out var idProp) ? idProp.GetString() ?? "" : "";
                int behavior = comp.TryGetProperty("rootcomponentbehavior", out var behProp) ? behProp.GetInt32() : -1;

                // 1 = Entity, behavior = 0 (IncludeAllSubcomponents)
                if (componentType == 1 && behavior == 0 && !string.IsNullOrEmpty(objectId))
                {
                    // Query entity logical name and check if it's OOB
                    try
                    {
                        var entRes = await _httpClient.GetAsync($"EntityDefinitions(MetadataId={objectId})?$select=LogicalName,IsCustomEntity").ConfigureAwait(false);
                        if (entRes.IsSuccessStatusCode)
                        {
                            using var entDoc = await entRes.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                            if (entDoc != null)
                            {
                                var entRoot = entDoc.RootElement;
                                string logicalName = entRoot.GetProperty("LogicalName").GetString() ?? "";
                                bool isCustom = entRoot.GetProperty("IsCustomEntity").GetBoolean();

                                if (!isCustom && !string.IsNullOrEmpty(logicalName))
                                {
                                    bloatedEntities.Add((objectId, logicalName));
                                    progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = $"Flagged bloated OOB table: {logicalName}", PercentComplete = 30 + (processedCount * 30.0 / totalComponents) });
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore individual entity metadata failures
                    }
                }
            }

            if (bloatedEntities.Count == 0)
            {
                progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = "No bloated OOB entities detected. Solution is already optimized.", PercentComplete = 100 });
                return report;
            }

            // 2. Perform direct distillation
            progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = $"Pruning {bloatedEntities.Count} bloated entities directly on server...", PercentComplete = 60 });
            
            double pruneStep = 40.0 / bloatedEntities.Count;
            double currentPrunePercent = 60.0;

            foreach (var ent in bloatedEntities)
            {
                currentPrunePercent += pruneStep;
                progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = $"Optimizing entity: {ent.LogicalName}...", PercentComplete = currentPrunePercent });

                // Step A: Remove solution component (unmanaged solution only)
                var removePayload = new
                {
                    ComponentId = ent.ObjectId,
                    ComponentType = 1,
                    SolutionUniqueName = solutionName
                };

                var remRes = await _httpClient.PostAsJsonAsync("RemoveSolutionComponent", removePayload, PascalCaseJsonOptions).ConfigureAwait(false);
                if (!remRes.IsSuccessStatusCode)
                {
                    string err = await remRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                    progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = $"[Warning] Failed to remove '{ent.LogicalName}': {err}", Status = ProgressStatus.Warning });
                    continue;
                }

                // Step B: Re-add entity component as shell only (behavior = 1 -> DoNotIncludeSubcomponents)
                var addPayload = new
                {
                    ComponentId = ent.ObjectId,
                    ComponentType = 1,
                    SolutionUniqueName = solutionName,
                    AddRequiredComponents = false,
                    DoNotIncludeSubcomponents = true
                };

                var addRes = await _httpClient.PostAsJsonAsync("AddSolutionComponent", addPayload, PascalCaseJsonOptions).ConfigureAwait(false);
                if (!addRes.IsSuccessStatusCode)
                {
                    string err = await addRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                    progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = $"[Warning] Failed to re-add shell '{ent.LogicalName}': {err}", Status = ProgressStatus.Warning });
                    continue;
                }

                report.ComponentsRemoved.Add(new PrunedComponent
                {
                    Type = "Entity",
                    LogicalName = ent.LogicalName,
                    ParentTable = "",
                    Reason = "Pruned entity behavior to 1 (DoNotIncludeSubcomponents) directly on server."
                });
            }

            progress?.Report(new ProgressUpdate { Stage = "Server Distill", Message = $"Distillation complete. Pruned {report.ComponentsRemoved.Count} components.", PercentComplete = 100 });
            return report;
        }
    }
}
