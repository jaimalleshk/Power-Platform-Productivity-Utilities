using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine
{
    public class PowerPlatformAdminApiCrawler
    {
        private static readonly string BapAdminApiEndpoint = "https://api.bap.microsoft.com/";
        private static readonly string PowerAppsAdminApiEndpoint = "https://api.powerapps.com/";

        public async Task CrawlPowerPlatformAdminSettingsAsync(HttpClient client, string envId, RawEnvData rawData)
        {
            if (string.IsNullOrWhiteSpace(envId)) return;

            try
            {
                // 1. Query Power Platform Admin API for Environment SKU, State, and Storage Capacity
                var resEnv = await client.GetAsync($"{BapAdminApiEndpoint}providers/Microsoft.BusinessAppPlatform/environments/{envId}?api-version=2021-04-01").ConfigureAwait(false);
                if (resEnv.IsSuccessStatusCode)
                {
                    using var doc = await resEnv.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("properties", out var props))
                    {
                        string sku = props.TryGetProperty("environmentSku", out var s) ? s.GetString() ?? "" : "";
                        string state = props.TryGetProperty("states", out var st) && st.TryGetProperty("runtime", out var rt) && rt.TryGetProperty("id", out var rtid) ? rtid.GetString() ?? "" : "";
                        string location = props.TryGetProperty("location", out var loc) ? loc.GetString() ?? "" : "";

                        rawData.AdminSettings["PPAdmin.EnvironmentGovernance"] = new Dictionary<string, string>
                        {
                            ["EnvironmentSku"] = sku,
                            ["State"] = state,
                            ["Location"] = location
                        };
                    }
                }

                // 2. Query Data Loss Prevention (DLP) Policies for this environment
                var resDlp = await client.GetAsync($"{PowerAppsAdminApiEndpoint}providers/Microsoft.PowerApps/scopes/admin/environments/{envId}/dlpPolicies?api-version=2021-04-01").ConfigureAwait(false);
                if (resDlp.IsSuccessStatusCode)
                {
                    using var doc = await resDlp.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var policies))
                    {
                        foreach (var pol in policies.EnumerateArray())
                        {
                            string polName = pol.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(polName)) continue;

                            rawData.AdminSettings[$"DlpPolicy.{polName}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = polName,
                                ["Status"] = "Enforced"
                            };
                        }
                    }
                }
            }
            catch
            {
                // Graceful fallback if BAP REST API token scope is missing
            }
        }
    }
}
