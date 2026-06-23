using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.SolutionDeepValidator.Models;

namespace Utilities.SolutionDeepValidator.Engine
{
    public class TargetEnvironmentCrawler
    {
        private readonly HttpClient _httpClient;
        private readonly bool _useSimulationMode;

        public TargetEnvironmentCrawler(HttpClient httpClient, bool useSimulationMode = false)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _useSimulationMode = useSimulationMode;
        }

        public async Task<TargetMetadataCache> CrawlTargetMetadataAsync(
            List<EntityManifestData> localEntities, 
            IProgress<ProgressUpdate>? progress = null)
        {
            var cache = new TargetMetadataCache();

            if (_useSimulationMode)
            {
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawling", Message = "(Simulation) Generating mock target metadata...", PercentComplete = 10 });
                await Task.Delay(500).ConfigureAwait(false);
                SimulatePopulateCache(cache, localEntities);
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawling", Message = "(Simulation) Metadata crawl complete.", PercentComplete = 100 });
                return cache;
            }

            double percentStep = 100.0 / 12.0;
            double currentPercent = 0.0;

            // Crawl Organization Info (Friendly Name)
            try
            {
                var orgItems = await GetPagedODataResultsAsync("organizations?$select=name", progress, "OrganizationInfo").ConfigureAwait(false);
                if (orgItems.Count > 0 && orgItems[0].TryGetProperty("name", out var nameProp))
                {
                    cache.OrganizationFriendlyName = nameProp.GetString() ?? "Unknown Env";
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"OrganizationInfo: {ex.Message}");
                cache.OrganizationFriendlyName = "Unknown Env";
            }

            // Helper to report progress
            void Report(string message)
            {
                currentPercent += percentStep;
                progress?.Report(new ProgressUpdate
                {
                    Stage = "Target Metadata Crawler",
                    Message = message,
                    PercentComplete = Math.Min(currentPercent, 100.0),
                    Status = ProgressStatus.Info
                });
            }

            // Source 1: Solutions
            try
            {
                Report("Loading solutions...");
                var items = await GetPagedODataResultsAsync("solutions?$select=uniquename,version,ismanaged,_publisherid_value", progress, "Solutions").ConfigureAwait(false);
                foreach (var el in items)
                {
                    cache.Solutions.Add(new SolutionCacheItem
                    {
                        UniqueName = el.TryGetProperty("uniquename", out var u) ? u.GetString() ?? "" : "",
                        Version = el.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                        IsManaged = el.TryGetProperty("ismanaged", out var m) && m.GetBoolean(),
                        PublisherId = el.TryGetProperty("_publisherid_value", out var p) ? p.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"Solutions: {ex.Message}");
            }

            // Source 2: Entities
            try
            {
                Report("Loading entity definitions...");
                var items = await GetPagedODataResultsAsync("EntityDefinitions?$select=LogicalName,DisplayName,IsCustomizable,CanCreateForms,CanCreateViews,IsCustomEntity", progress, "Entities").ConfigureAwait(false);
                foreach (var el in items)
                {
                    string displayName = "";
                    if (el.TryGetProperty("DisplayName", out var dispProp) && dispProp.ValueKind == JsonValueKind.Object)
                    {
                        if (dispProp.TryGetProperty("UserLocalizedLabel", out var labelProp) && labelProp.ValueKind == JsonValueKind.Object)
                        {
                            displayName = labelProp.TryGetProperty("Label", out var lbl) ? lbl.GetString() ?? "" : "";
                        }
                    }

                    cache.Entities.Add(new EntityCacheItem
                    {
                        LogicalName = el.TryGetProperty("LogicalName", out var l) ? l.GetString() ?? "" : "",
                        DisplayName = displayName,
                        IsCustomizable = !el.TryGetProperty("IsCustomizable", out var cust) || cust.GetProperty("Value").GetBoolean(),
                        CanCreateForms = !el.TryGetProperty("CanCreateForms", out var forms) || forms.GetProperty("Value").GetBoolean(),
                        CanCreateViews = !el.TryGetProperty("CanCreateViews", out var views) || views.GetProperty("Value").GetBoolean(),
                        IsCustomEntity = el.TryGetProperty("IsCustomEntity", out var ce) && ce.GetBoolean()
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"Entities: {ex.Message}");
            }

            // Source 3: Attributes (Queried dynamically for local entities to optimize payload sizes)
            try
            {
                Report("Loading attribute metadata for evaluation tables...");
                foreach (var localEnt in localEntities)
                {
                    var attrList = new List<AttributeCacheItem>();
                    try
                    {
                        var items = await GetPagedODataResultsAsync($"EntityDefinitions(LogicalName='{localEnt.LogicalName}')/Attributes?$select=LogicalName,AttributeType,IsCustomizable,MaxLength,Precision", progress, $"Attributes-{localEnt.LogicalName}").ConfigureAwait(false);
                        foreach (var el in items)
                        {
                            int maxLength = 0;
                            if (el.TryGetProperty("MaxLength", out var maxProp) && maxProp.ValueKind == JsonValueKind.Number)
                            {
                                maxLength = maxProp.GetInt32();
                            }
                            int precision = 0;
                            if (el.TryGetProperty("Precision", out var precProp) && precProp.ValueKind == JsonValueKind.Number)
                            {
                                precision = precProp.GetInt32();
                            }

                            attrList.Add(new AttributeCacheItem
                            {
                                LogicalName = el.TryGetProperty("LogicalName", out var a) ? a.GetString() ?? "" : "",
                                AttributeType = el.TryGetProperty("AttributeType", out var t) ? t.GetString() ?? "" : "",
                                IsCustomizable = !el.TryGetProperty("IsCustomizable", out var cust) || cust.GetProperty("Value").GetBoolean(),
                                MaxLength = maxLength,
                                Precision = precision
                            });
                        }
                        cache.Attributes[localEnt.LogicalName] = attrList;
                    }
                    catch (Exception ex)
                    {
                        cache.MetadataGaps.Add($"Attributes for {localEnt.LogicalName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"Attributes crawl failed: {ex.Message}");
            }

            // Source 4: Relationships
            try
            {
                Report("Loading OneToMany relationships...");
                var oneToManyItems = await GetPagedODataResultsAsync(
                    "RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?$select=SchemaName,ReferencedEntity,ReferencingEntity", 
                    progress, "Relationships-1N").ConfigureAwait(false);
                foreach (var el in oneToManyItems)
                {
                    cache.Relationships.Add(new RelationshipCacheItem
                    {
                        SchemaName = el.TryGetProperty("SchemaName", out var s) ? s.GetString() ?? "" : "",
                        Entity1LogicalName = el.TryGetProperty("ReferencedEntity", out var e1) ? e1.GetString() ?? "" : "",
                        Entity2LogicalName = el.TryGetProperty("ReferencingEntity", out var e2) ? e2.GetString() ?? "" : ""
                    });
                }

                Report("Loading ManyToMany relationships...");
                var manyToManyItems = await GetPagedODataResultsAsync(
                    "RelationshipDefinitions/Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata?$select=SchemaName,Entity1LogicalName,Entity2LogicalName", 
                    progress, "Relationships-NN").ConfigureAwait(false);
                foreach (var el in manyToManyItems)
                {
                    cache.Relationships.Add(new RelationshipCacheItem
                    {
                        SchemaName = el.TryGetProperty("SchemaName", out var s) ? s.GetString() ?? "" : "",
                        Entity1LogicalName = el.TryGetProperty("Entity1LogicalName", out var e1) ? e1.GetString() ?? "" : "",
                        Entity2LogicalName = el.TryGetProperty("Entity2LogicalName", out var e2) ? e2.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"Relationships: {ex.Message}");
            }

            // Source 5: OptionSets
            try
            {
                Report("Loading global choices...");
                var items = await GetPagedODataResultsAsync("GlobalOptionSetDefinitions?$select=Name", progress, "OptionSets").ConfigureAwait(false);
                foreach (var el in items)
                {
                    cache.OptionSets.Add(new OptionSetCacheItem
                    {
                        Name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"OptionSets: {ex.Message}");
            }

            // Source 6: Workflows
            try
            {
                Report("Loading workflows and processes...");
                var items = await GetPagedODataResultsAsync("workflows?$select=workflowid,name,primaryentity,category,statecode", progress, "Workflows").ConfigureAwait(false);
                foreach (var el in items)
                {
                    cache.Workflows.Add(new WorkflowCacheItem
                    {
                        WorkflowId = el.TryGetProperty("workflowid", out var w) ? w.GetString() ?? "" : "",
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        PrimaryEntity = el.TryGetProperty("primaryentity", out var p) ? p.GetString() ?? "" : "",
                        Category = el.TryGetProperty("category", out var c) ? c.GetInt32() : 0,
                        StateCode = el.TryGetProperty("statecode", out var sc) ? sc.GetInt32() : 0
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"Workflows: {ex.Message}");
            }

            // Source 7: Plugin Assemblies
            try
            {
                Report("Loading plugin assemblies...");
                var items = await GetPagedODataResultsAsync("pluginassemblies?$select=pluginassemblyid,name", progress, "Assemblies").ConfigureAwait(false);
                foreach (var el in items)
                {
                    cache.PluginAssemblies.Add(new PluginAssemblyCacheItem
                    {
                        PluginAssemblyId = el.TryGetProperty("pluginassemblyid", out var i) ? i.GetString() ?? "" : "",
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"PluginAssemblies: {ex.Message}");
            }

            // Source 8: Plugin Steps
            try
            {
                Report("Loading SDK message processing steps...");
                string expandQuery = "sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid,name&$expand=sdkmessagefilterid($select=primaryobjecttypecode),sdkmessageid($select=name)";
                var items = await GetPagedODataResultsAsync(expandQuery, progress, "PluginSteps").ConfigureAwait(false);
                foreach (var el in items)
                {
                    string targetEntity = "";
                    if (el.TryGetProperty("sdkmessagefilterid", out var filterProp) && filterProp.ValueKind == JsonValueKind.Object)
                    {
                        targetEntity = filterProp.TryGetProperty("primaryobjecttypecode", out var codeProp) ? codeProp.GetString() ?? "" : "";
                    }

                    string sdkMessageName = "";
                    if (el.TryGetProperty("sdkmessageid", out var msgProp) && msgProp.ValueKind == JsonValueKind.Object)
                    {
                        sdkMessageName = msgProp.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    }

                    cache.PluginSteps.Add(new PluginStepCacheItem
                    {
                        SdkMessageProcessingStepId = el.TryGetProperty("sdkmessageprocessingstepid", out var s) ? s.GetString() ?? "" : "",
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        SdkMessageName = sdkMessageName,
                        TargetEntity = targetEntity
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"PluginSteps: {ex.Message}");
            }

            // Source 9: Web Resources
            try
            {
                Report("Loading web resources...");
                var items = await GetPagedODataResultsAsync("webresourceset?$select=webresourceid,name", progress, "WebResources").ConfigureAwait(false);
                foreach (var el in items)
                {
                    cache.WebResources.Add(new WebResourceCacheItem
                    {
                        WebResourceId = el.TryGetProperty("webresourceid", out var i) ? i.GetString() ?? "" : "",
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"WebResources: {ex.Message}");
            }

            // Source 10: Security Roles
            try
            {
                Report("Loading security roles...");
                var items = await GetPagedODataResultsAsync("roles?$select=roleid,name", progress, "Roles").ConfigureAwait(false);
                foreach (var el in items)
                {
                    cache.SecurityRoles.Add(new SecurityRoleCacheItem
                    {
                        RoleId = el.TryGetProperty("roleid", out var r) ? r.GetString() ?? "" : "",
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"SecurityRoles: {ex.Message}");
            }

            // Source 11: Connection References
            try
            {
                Report("Loading connection references...");
                var items = await GetPagedODataResultsAsync("connectionreferences?$select=connectionreferenceid,connectionreferencelogicalname,connectorid", progress, "ConnectionRefs").ConfigureAwait(false);
                foreach (var el in items)
                {
                    cache.ConnectionReferences.Add(new ConnectionRefCacheItem
                    {
                        ConnectionReferenceId = el.TryGetProperty("connectionreferenceid", out var i) ? i.GetString() ?? "" : "",
                        ConnectionReferenceLogicalName = el.TryGetProperty("connectionreferencelogicalname", out var l) ? l.GetString() ?? "" : "",
                        ConnectorId = el.TryGetProperty("connectorid", out var c) ? c.GetString() ?? "" : ""
                    });
                }
            }
            catch (Exception ex)
            {
                cache.MetadataGaps.Add($"ConnectionReferences: {ex.Message}");
            }

            progress?.Report(new ProgressUpdate { Stage = "Target Metadata Crawler", Message = "Target environment crawl completed successfully.", PercentComplete = 100.0 });
            return cache;
        }

        private async Task<List<JsonElement>> GetPagedODataResultsAsync(
            string url, 
            IProgress<ProgressUpdate>? progress, 
            string stage)
        {
            var list = new List<JsonElement>();
            string? currentUrl = url;

            while (!string.IsNullOrEmpty(currentUrl))
            {
                var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                request.Headers.Add("Prefer", "odata.maxpagesize=5000");

                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string errContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new Exception($"OData query failed on stage {stage}: {response.StatusCode}, details: {errContent}");
                }

                using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                if (doc == null) break;

                var root = doc.RootElement;
                if (root.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in valueProp.EnumerateArray())
                    {
                        list.Add(item.Clone());
                    }
                }

                currentUrl = null;
                if (root.TryGetProperty("@odata.nextLink", out var nextProp))
                {
                    string nextLink = nextProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(nextLink))
                    {
                        // OData nextLink can be absolute. If HttpClient has a base address and it matches, we can make it relative if needed, 
                        // but HttpClient supports absolute URLs directly as well.
                        currentUrl = nextLink;
                        progress?.Report(new ProgressUpdate
                        {
                            Stage = $"Crawling {stage}",
                            Message = $"Fetched {list.Count} records. Reading next page...",
                            PercentComplete = -1
                        });
                    }
                }
            }

            return list;
        }

        private void SimulatePopulateCache(TargetMetadataCache cache, List<EntityManifestData> localEntities)
        {
            cache.OrganizationFriendlyName = "Verizon Sandbox UAT";
            // Solutions
            cache.Solutions.Add(new SolutionCacheItem { UniqueName = "CorePrerequisites", Version = "1.0.0.0", IsManaged = true });
            cache.Solutions.Add(new SolutionCacheItem { UniqueName = "OmnichannelBase", Version = "2.1.0.0", IsManaged = true });

            // Entities
            cache.Entities.Add(new EntityCacheItem { LogicalName = "account", DisplayName = "Account", IsCustomizable = true, CanCreateForms = true, CanCreateViews = true, IsCustomEntity = false });
            cache.Entities.Add(new EntityCacheItem { LogicalName = "contact", DisplayName = "Contact", IsCustomizable = true, CanCreateForms = true, CanCreateViews = true, IsCustomEntity = false });
            cache.Entities.Add(new EntityCacheItem { LogicalName = "opportunity", DisplayName = "Opportunity", IsCustomizable = true, CanCreateForms = true, CanCreateViews = true, IsCustomEntity = false });
            cache.Entities.Add(new EntityCacheItem { LogicalName = "new_customtable", DisplayName = "Custom Table", IsCustomizable = true, CanCreateForms = true, CanCreateViews = true, IsCustomEntity = true });

            // Attributes
            foreach (var ent in localEntities)
            {
                var attrs = new List<AttributeCacheItem>();
                if (ent.LogicalName.Equals("account", StringComparison.OrdinalIgnoreCase))
                {
                    attrs.Add(new AttributeCacheItem { LogicalName = "accountid", AttributeType = "Uniqueidentifier", IsCustomizable = false });
                    attrs.Add(new AttributeCacheItem { LogicalName = "name", AttributeType = "String", IsCustomizable = true, MaxLength = 160 });
                    attrs.Add(new AttributeCacheItem { LogicalName = "telephone1", AttributeType = "String", IsCustomizable = true, MaxLength = 50 });
                }
                else if (ent.LogicalName.Equals("opportunity", StringComparison.OrdinalIgnoreCase))
                {
                    attrs.Add(new AttributeCacheItem { LogicalName = "opportunityid", AttributeType = "Uniqueidentifier", IsCustomizable = false });
                    attrs.Add(new AttributeCacheItem { LogicalName = "name", AttributeType = "String", IsCustomizable = true, MaxLength = 300 });
                    // Mismatch attribute simulation (Source: Decimal, Target: Money)
                    attrs.Add(new AttributeCacheItem { LogicalName = "new_transactionamount", AttributeType = "Money", IsCustomizable = true });
                }
                cache.Attributes[ent.LogicalName] = attrs;
            }

            // OptionSets
            cache.OptionSets.Add(new OptionSetCacheItem { Name = "custom_colors" });

            // Workflows
            cache.Workflows.Add(new WorkflowCacheItem { WorkflowId = "W001-GUID", Name = "Calculate Totals Workflow", PrimaryEntity = "opportunity", Category = 0, StateCode = 1 });

            // WebResources
            cache.WebResources.Add(new WebResourceCacheItem { WebResourceId = "custom_library.js", Name = "custom_library.js" });

            // Plugin Assemblies
            cache.PluginAssemblies.Add(new PluginAssemblyCacheItem { PluginAssemblyId = "A001-GUID", Name = "MyPluginAssembly" });

            // Roles
            cache.SecurityRoles.Add(new SecurityRoleCacheItem { RoleId = "R001-GUID", Name = "Customer Service Representative" });

            // Connection References
            cache.ConnectionReferences.Add(new ConnectionRefCacheItem { ConnectionReferenceId = "CR001-GUID", ConnectionReferenceLogicalName = "new_CDSConnection", ConnectorId = "shared_commondataserviceforapps" });
        }
    }
}
