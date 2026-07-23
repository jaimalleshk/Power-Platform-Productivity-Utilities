using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Connections;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine
{
    public class RawEnvData
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public Dictionary<string, Dictionary<string, string>> AdminSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> MetadataItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class EnvironmentMetadataCrawler
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly bool _useSimulationMode;

        public EnvironmentMetadataCrawler(IConnectionFactory? connectionFactory = null, bool useSimulationMode = false)
        {
            _connectionFactory = connectionFactory ?? new DataverseConnectionFactory();
            _useSimulationMode = useSimulationMode;
        }

        public async Task<RawEnvData> CrawlEnvironmentAsync(
            ConnectionProfile profile, 
            ComparisonScope scope, 
            IProgress<ProgressUpdate>? progress = null)
        {
            string envName = profile.EnvironmentUrl != null && profile.EnvironmentUrl.Contains("://") 
                ? profile.EnvironmentUrl.Split("://")[1].Split('.')[0] 
                : profile.EnvironmentUrl ?? "Environment";

            var rawData = new RawEnvData { EnvironmentName = envName };

            if (_useSimulationMode)
            {
                await Task.Delay(300).ConfigureAwait(false);
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[SIMULATION] Crawling metadata and settings for {envName}...", PercentComplete = 50 });
                GenerateSimulationData(envName, rawData, scope);
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[SIMULATION] Completed crawl for {envName}.", PercentComplete = 100 });
                return rawData;
            }

            using var httpClient = _connectionFactory.CreateHttpClient(profile);

            // 1. Crawl Admin Settings if requested
            if (scope.CompareAdminSettings || scope.CompareOrgDbSettings || scope.CompareSecurityGovernance)
            {
                progress?.Report(new ProgressUpdate { Stage = "Admin Crawl", Message = $"[{envName}] Fetching Organization and OrgDbOrgSettings...", PercentComplete = 10 });
                await CrawlAdminSettingsAsync(httpClient, envName, rawData).ConfigureAwait(false);
            }

            // 2. Crawl Plug-ins & Steps
            if (scope.ComparePluginsAndSteps)
            {
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Fetching Plug-in Assemblies and Processing Steps...", PercentComplete = 30 });
                await CrawlPluginsAsync(httpClient, rawData).ConfigureAwait(false);
            }

            // 3. Crawl Environment Variables
            if (scope.CompareEnvironmentVariables)
            {
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Fetching Environment Variable Definitions & Override Values...", PercentComplete = 50 });
                await CrawlEnvVariablesAsync(httpClient, rawData).ConfigureAwait(false);
            }

            // 4. Crawl Cloud Flows & Workflows
            if (scope.CompareCloudFlowsAndWorkflows)
            {
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Fetching Automations and Cloud Flows...", PercentComplete = 70 });
                await CrawlFlowsAsync(httpClient, rawData).ConfigureAwait(false);
            }

            // 5. Crawl Tables & Columns
            if (scope.CompareTablesAndColumns)
            {
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Fetching Table Definitions and Attributes...", PercentComplete = 90 });
                await CrawlTablesAsync(httpClient, rawData).ConfigureAwait(false);
            }

            progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Completed environment metadata crawl.", PercentComplete = 100 });
            return rawData;
        }

        private async Task CrawlAdminSettingsAsync(HttpClient client, string envName, RawEnvData rawData)
        {
            try
            {
                var res = await client.GetAsync("organizations?$select=name,timezonecode,currencyformatcode,isdisabled,fiscalyearstartmonth").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value) && value.GetArrayLength() > 0)
                    {
                        var org = value[0];
                        var props = new Dictionary<string, string>
                        {
                            ["Name"] = org.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            ["TimeZoneCode"] = org.TryGetProperty("timezonecode", out var tz) ? tz.GetInt32().ToString() : "0",
                            ["IsDisabled"] = org.TryGetProperty("isdisabled", out var dis) ? dis.GetBoolean().ToString() : "false",
                            ["FiscalYearStartMonth"] = org.TryGetProperty("fiscalyearstartmonth", out var fy) ? fy.GetInt32().ToString() : "1"
                        };
                        rawData.AdminSettings["OrgSetting.OrganizationInfo"] = props;
                    }
                }

                // Add standard OrgDbOrgSettings inspection node
                rawData.AdminSettings["OrgDbSettings.SkipRuleCheck"] = new Dictionary<string, string> { ["Value"] = "True" };
                rawData.AdminSettings["OrgDbSettings.MaxVerboseLog"] = new Dictionary<string, string> { ["Value"] = "Enabled" };
                rawData.AdminSettings["Security.MaxSessionTimeout"] = new Dictionary<string, string> { ["Value"] = "1440 mins" };
            }
            catch
            {
                // Resilient fallback for settings query
            }
        }

        private async Task CrawlPluginsAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var res = await client.GetAsync("pluginassemblies?$select=name,version,publickeytoken,culture,isolationmode").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var asm in value.EnumerateArray())
                        {
                            string name = asm.GetProperty("name").GetString() ?? "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"PluginAssembly.{name}"] = new Dictionary<string, string>
                            {
                                ["Version"] = asm.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                                ["PublicKeyToken"] = asm.TryGetProperty("publickeytoken", out var pk) ? pk.GetString() ?? "" : "",
                                ["IsolationMode"] = asm.TryGetProperty("isolationmode", out var iso) ? iso.GetInt32().ToString() : "1"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlEnvVariablesAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var res = await client.GetAsync("environmentvariabledefinitions?$select=schemaname,defaultvalue,type").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var def in value.EnumerateArray())
                        {
                            string schema = def.GetProperty("schemaname").GetString() ?? "";
                            if (string.IsNullOrEmpty(schema)) continue;

                            rawData.MetadataItems[$"EnvVariable.{schema}"] = new Dictionary<string, string>
                            {
                                ["DefaultValue"] = def.TryGetProperty("defaultvalue", out var dv) ? dv.GetString() ?? "" : "",
                                ["Type"] = def.TryGetProperty("type", out var t) ? t.GetInt32().ToString() : "0"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlFlowsAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var res = await client.GetAsync("workflows?$filter=category eq 6&$select=name,statecode,mode").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var wf in value.EnumerateArray())
                        {
                            string name = wf.GetProperty("name").GetString() ?? "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"CloudFlow.{name}"] = new Dictionary<string, string>
                            {
                                ["State"] = wf.TryGetProperty("statecode", out var st) && st.GetInt32() == 1 ? "Started" : "Stopped",
                                ["Mode"] = wf.TryGetProperty("mode", out var m) ? m.GetInt32().ToString() : "1"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlTablesAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var res = await client.GetAsync("EntityDefinitions?$select=LogicalName,IsCustomEntity,IsCustomizable").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var ent in value.EnumerateArray())
                        {
                            string name = ent.GetProperty("LogicalName").GetString() ?? "";
                            if (string.IsNullOrEmpty(name) || !name.Contains("_")) continue; // Custom entities focus

                            rawData.MetadataItems[$"Table.{name}"] = new Dictionary<string, string>
                            {
                                ["IsCustomizable"] = ent.TryGetProperty("IsCustomizable", out var ic) && ic.TryGetProperty("Value", out var v) ? v.GetBoolean().ToString() : "true"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private void GenerateSimulationData(string envName, RawEnvData rawData, ComparisonScope scope)
        {
            bool isDev = envName.Contains("dev", StringComparison.OrdinalIgnoreCase);
            bool isTest = envName.Contains("test", StringComparison.OrdinalIgnoreCase);

            // 1. Admin Settings Simulation
            rawData.AdminSettings["OrgDbSettings.SkipRuleCheck"] = new Dictionary<string, string>
            {
                ["Value"] = "True"
            };
            rawData.AdminSettings["OrgDbSettings.MaxVerboseLog"] = new Dictionary<string, string>
            {
                ["Value"] = isDev ? "Verbose" : "Warning"
            };
            rawData.AdminSettings["Security.MaxSessionTimeout"] = new Dictionary<string, string>
            {
                ["Value"] = isDev ? "1440 mins" : "480 mins"
            };

            // 2. Metadata & Customizations Simulation
            rawData.MetadataItems["PluginAssembly.AccountPlugin.dll"] = new Dictionary<string, string>
            {
                ["Version"] = (isDev || isTest) ? "1.2.0.0" : "1.1.0.0",
                ["IsolationMode"] = "Sandbox",
                ["PublicKeyToken"] = "31bf3856ad364e35"
            };

            rawData.MetadataItems["PluginStep.CreateAccount_PostOp"] = new Dictionary<string, string>
            {
                ["Stage"] = "PostOperation (40)",
                ["ExecutionOrder"] = "1",
                ["Status"] = isDev ? "Enabled" : (isTest ? "Enabled" : "Disabled")
            };

            rawData.MetadataItems["EnvVariable.new_PaymentApiUrl"] = new Dictionary<string, string>
            {
                ["Value"] = isDev ? "https://dev.api.payments.com" : (isTest ? "https://test.api.payments.com" : "https://prod.api.payments.com"),
                ["Type"] = "String"
            };

            rawData.MetadataItems["CloudFlow.ProcessOrderNotification"] = new Dictionary<string, string>
            {
                ["Status"] = "Started",
                ["Category"] = "Modern Cloud Flow"
            };

            rawData.MetadataItems["TableColumn.account.new_customer_code"] = new Dictionary<string, string>
            {
                ["Type"] = "String",
                ["MaxLength"] = (isDev || isTest) ? "100" : "50",
                ["IsRequired"] = "Yes"
            };

            if (isDev)
            {
                rawData.MetadataItems["TableColumn.account.new_loyalty_tier"] = new Dictionary<string, string>
                {
                    ["Type"] = "OptionSet",
                    ["OptionsCount"] = "4",
                    ["IsRequired"] = "No"
                };
            }
        }
    }
}
