using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine.Providers
{
    public class PowerPagesProvider : IComparisonProvider
    {
        public string ProviderName => "PowerPagesSiteProvider";
        public string TargetCategory => "PowerPages";
        public int ExecutionOrder => 50;

        public async Task CrawlAsync(HttpClient client, string envName, RawEnvData rawData, ComparisonScope scope)
        {
            try
            {
                var res = await client.GetAsync("powerpagesites?$select=name,content,statecode").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var site in value.EnumerateArray())
                        {
                            string name = site.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"PowerPagesSite.{name}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = name,
                                ["Status"] = site.TryGetProperty("statecode", out var st) && st.GetInt32() == 0 ? "Active" : "Inactive"
                            };
                        }
                    }
                }
            }
            catch
            {
                // Fallback for environments without Power Pages installed
            }
        }
    }
}
