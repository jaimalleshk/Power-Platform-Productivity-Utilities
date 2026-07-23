using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine.Providers
{
    public class AiModelsProvider : IComparisonProvider
    {
        public string ProviderName => "AiModelsProvider";
        public string TargetCategory => "AIModels";
        public int ExecutionOrder => 60;

        public async Task CrawlAsync(HttpClient client, string envName, RawEnvData rawData, ComparisonScope scope)
        {
            try
            {
                var res = await client.GetAsync("msdyn_aimodels?$select=msdyn_name,msdyn_modeltype,statecode").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var model in value.EnumerateArray())
                        {
                            string name = model.TryGetProperty("msdyn_name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"AIModel.{name}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = name,
                                ["ModelType"] = model.TryGetProperty("msdyn_modeltype", out var mt) ? mt.GetString() ?? "" : "",
                                ["Status"] = model.TryGetProperty("statecode", out var st) && st.GetInt32() == 0 ? "Active" : "Inactive"
                            };
                        }
                    }
                }
            }
            catch
            {
                // Fallback for environments without AI Builder models
            }
        }
    }
}
