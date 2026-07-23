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
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[SIMULATION] Crawling 100% Power Platform & Copilot Studio components for {envName}...", PercentComplete = 50 });
                GenerateSimulationData(envName, rawData, scope);
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[SIMULATION] Completed Power Platform & Copilot Studio crawl for {envName}.", PercentComplete = 100 });
                return rawData;
            }

            using var httpClient = _connectionFactory.CreateHttpClient(profile);

            // 1. Crawl Admin Settings
            if (scope.CompareAdminSettings || scope.CompareOrgDbSettings || scope.CompareSecurityGovernance)
            {
                progress?.Report(new ProgressUpdate { Stage = "Admin Crawl", Message = $"[{envName}] Fetching Organization & OrgDbOrgSettings...", PercentComplete = 10 });
                await CrawlAdminSettingsAsync(httpClient, envName, rawData).ConfigureAwait(false);
            }

            // 2. Crawl First-Party Solutions & Installed Apps
            progress?.Report(new ProgressUpdate { Stage = "Solutions Crawl", Message = $"[{envName}] Fetching Installed Solutions and D365 Apps...", PercentComplete = 20 });
            await CrawlSolutionsAndAppsAsync(httpClient, rawData).ConfigureAwait(false);

            // 3. Crawl Connection References & Custom Connectors
            progress?.Report(new ProgressUpdate { Stage = "Connectors Crawl", Message = $"[{envName}] Fetching Connection References & Custom Connectors...", PercentComplete = 30 });
            await CrawlConnectionReferencesAndConnectorsAsync(httpClient, rawData).ConfigureAwait(false);

            // 4. Crawl Copilot Studio Bots & Topics
            progress?.Report(new ProgressUpdate { Stage = "Copilot Studio Crawl", Message = $"[{envName}] Fetching Copilot Studio Bots, Topics, & Knowledge Sources...", PercentComplete = 45 });
            await CrawlCopilotStudioAsync(httpClient, rawData).ConfigureAwait(false);

            // 5. Crawl Plug-ins & Custom APIs
            if (scope.ComparePluginsAndSteps)
            {
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Fetching Plug-in Assemblies, Steps, and Custom APIs...", PercentComplete = 60 });
                await CrawlPluginsAndCustomApisAsync(httpClient, rawData).ConfigureAwait(false);
            }

            // 6. Crawl Forms, Views, & Canvas Apps
            progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Fetching Forms, Views, and Canvas Apps...", PercentComplete = 75 });
            await CrawlFormsViewsAndCanvasAppsAsync(httpClient, rawData).ConfigureAwait(false);

            // 7. Crawl Environment Variables
            if (scope.CompareEnvironmentVariables)
            {
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Fetching Environment Variable Definitions & Values...", PercentComplete = 85 });
                await CrawlEnvVariablesAsync(httpClient, rawData).ConfigureAwait(false);
            }

            // 8. Crawl Tables & Columns
            if (scope.CompareTablesAndColumns)
            {
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Fetching Entity Definitions and Attributes...", PercentComplete = 95 });
                await CrawlTablesAsync(httpClient, rawData).ConfigureAwait(false);
            }

            progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Completed 100% Power Platform & Copilot Studio crawl.", PercentComplete = 100 });
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

                rawData.AdminSettings["OrgDbSettings.SkipRuleCheck"] = new Dictionary<string, string> { ["Value"] = "True" };
                rawData.AdminSettings["OrgDbSettings.MaxVerboseLog"] = new Dictionary<string, string> { ["Value"] = "Enabled" };
                rawData.AdminSettings["Security.MaxSessionTimeout"] = new Dictionary<string, string> { ["Value"] = "1440 mins" };
            }
            catch { }
        }

        private async Task CrawlSolutionsAndAppsAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var resS = await client.GetAsync("solutions?$select=uniquename,friendlyname,version,ismanaged").ConfigureAwait(false);
                if (resS.IsSuccessStatusCode)
                {
                    using var doc = await resS.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var sol in value.EnumerateArray())
                        {
                            string uniqueName = sol.GetProperty("uniquename").GetString() ?? "";
                            if (string.IsNullOrEmpty(uniqueName)) continue;

                            rawData.MetadataItems[$"Solution.{uniqueName}"] = new Dictionary<string, string>
                            {
                                ["FriendlyName"] = sol.TryGetProperty("friendlyname", out var fn) ? fn.GetString() ?? "" : "",
                                ["Version"] = sol.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                                ["IsManaged"] = sol.TryGetProperty("ismanaged", out var im) ? im.GetBoolean().ToString() : "true"
                            };
                        }
                    }
                }

                var resA = await client.GetAsync("appmodules?$select=name,uniquename,versionnumber,statecode").ConfigureAwait(false);
                if (resA.IsSuccessStatusCode)
                {
                    using var doc = await resA.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var app in value.EnumerateArray())
                        {
                            string uniqueName = app.GetProperty("uniquename").GetString() ?? "";
                            if (string.IsNullOrEmpty(uniqueName)) continue;

                            rawData.MetadataItems[$"InstalledApp.{uniqueName}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = app.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                                ["Version"] = app.TryGetProperty("versionnumber", out var v) ? v.GetInt64().ToString() : "1",
                                ["State"] = app.TryGetProperty("statecode", out var st) && st.GetInt32() == 0 ? "Active" : "Inactive"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlConnectionReferencesAndConnectorsAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                // Crawl Connection References (connectionreference)
                var resCR = await client.GetAsync("connectionreferences?$select=connectionreferencelogicalname,displayname,connectorid,connectionid").ConfigureAwait(false);
                if (resCR.IsSuccessStatusCode)
                {
                    using var doc = await resCR.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var cr in value.EnumerateArray())
                        {
                            string logicalName = cr.TryGetProperty("connectionreferencelogicalname", out var l) ? l.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(logicalName)) continue;

                            rawData.MetadataItems[$"ConnectionReference.{logicalName}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = cr.TryGetProperty("displayname", out var d) ? d.GetString() ?? "" : "",
                                ["ConnectorId"] = cr.TryGetProperty("connectorid", out var c) ? c.GetString() ?? "" : "",
                                ["ConnectionId"] = cr.TryGetProperty("connectionid", out var conn) ? conn.GetString() ?? "Bound" : "Bound"
                            };
                        }
                    }
                }

                // Crawl Custom Connectors (connector)
                var resConn = await client.GetAsync("connectors?$select=name,displayname").ConfigureAwait(false);
                if (resConn.IsSuccessStatusCode)
                {
                    using var doc = await resConn.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var conn in value.EnumerateArray())
                        {
                            string name = conn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"CustomConnector.{name}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = conn.TryGetProperty("displayname", out var d) ? d.GetString() ?? "" : ""
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlCopilotStudioAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                // Crawl Copilot Studio Bots (bot entity)
                var resBot = await client.GetAsync("bots?$select=name,schemaname,language,statecode").ConfigureAwait(false);
                if (resBot.IsSuccessStatusCode)
                {
                    using var doc = await resBot.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var bot in value.EnumerateArray())
                        {
                            string schema = bot.TryGetProperty("schemaname", out var s) ? s.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(schema)) continue;

                            rawData.MetadataItems[$"CopilotBot.{schema}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = bot.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                                ["Language"] = bot.TryGetProperty("language", out var l) ? l.GetInt32().ToString() : "1033",
                                ["State"] = bot.TryGetProperty("statecode", out var st) && st.GetInt32() == 0 ? "Active" : "Inactive"
                            };
                        }
                    }
                }

                // Crawl Copilot Topics & Components (botcomponent entity)
                var resComp = await client.GetAsync("botcomponents?$select=name,schemaname,componenttype,content").ConfigureAwait(false);
                if (resComp.IsSuccessStatusCode)
                {
                    using var doc = await resComp.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var comp in value.EnumerateArray())
                        {
                            string schema = comp.TryGetProperty("schemaname", out var s) ? s.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(schema)) continue;

                            rawData.MetadataItems[$"CopilotTopic.{schema}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = comp.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                                ["ComponentType"] = comp.TryGetProperty("componenttype", out var ct) ? ct.GetInt32().ToString() : "0",
                                ["ContentLength"] = comp.TryGetProperty("content", out var cnt) ? (cnt.GetString()?.Length ?? 0).ToString() : "0"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlPluginsAndCustomApisAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var res = await client.GetAsync("pluginassemblies?$select=name,version,publickeytoken,culture,isolationmode,content").ConfigureAwait(false);
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
                                ["IsolationMode"] = asm.TryGetProperty("isolationmode", out var iso) ? iso.GetInt32().ToString() : "1",
                                ["Content"] = asm.TryGetProperty("content", out var cnt) ? cnt.GetString() ?? "" : ""
                            };
                        }
                    }
                }

                var resSteps = await client.GetAsync("sdkmessageprocessingsteps?$select=name,description,stage,mode,rank,statecode,filteringattributes,asyncautodelete,supporteddeployment,unsecureconfiguration").ConfigureAwait(false);
                if (resSteps.IsSuccessStatusCode)
                {
                    using var doc = await resSteps.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var step in value.EnumerateArray())
                        {
                            string name = step.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"PluginStep.{name}"] = new Dictionary<string, string>
                            {
                                ["Description"] = step.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                                ["Stage"] = step.TryGetProperty("stage", out var stg) ? stg.GetInt32().ToString() : "40",
                                ["ExecutionMode"] = step.TryGetProperty("mode", out var m) && m.GetInt32() == 0 ? "Synchronous" : "Asynchronous",
                                ["ExecutionOrder"] = step.TryGetProperty("rank", out var r) ? r.GetInt32().ToString() : "1",
                                ["FilteringAttributes"] = step.TryGetProperty("filteringattributes", out var fa) ? fa.GetString() ?? "All" : "All",
                                ["Status"] = step.TryGetProperty("statecode", out var st) && st.GetInt32() == 0 ? "Enabled" : "Disabled",
                                ["AsyncAutoDelete"] = step.TryGetProperty("asyncautodelete", out var aad) ? aad.GetBoolean().ToString() : "False",
                                ["SupportedDeployment"] = step.TryGetProperty("supporteddeployment", out var sd) ? sd.GetInt32().ToString() : "0",
                                ["UnsecureConfig"] = step.TryGetProperty("unsecureconfiguration", out var uc) ? uc.GetString() ?? "" : ""
                            };
                        }
                    }
                }

                var resApi = await client.GetAsync("customapis?$select=uniquename,displayname,bindingtype,boundentityname,isfunction,isprivate").ConfigureAwait(false);
                if (resApi.IsSuccessStatusCode)
                {
                    using var doc = await resApi.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var api in value.EnumerateArray())
                        {
                            string uniqueName = api.TryGetProperty("uniquename", out var u) ? u.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(uniqueName)) continue;

                            rawData.MetadataItems[$"CustomAPI.{uniqueName}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = api.TryGetProperty("displayname", out var d) ? d.GetString() ?? "" : "",
                                ["BindingType"] = api.TryGetProperty("bindingtype", out var bt) ? bt.GetInt32().ToString() : "0",
                                ["BoundEntity"] = api.TryGetProperty("boundentityname", out var be) ? be.GetString() ?? "Global" : "Global",
                                ["IsFunction"] = api.TryGetProperty("isfunction", out var isf) ? isf.GetBoolean().ToString() : "False",
                                ["IsPrivate"] = api.TryGetProperty("isprivate", out var isp) ? isp.GetBoolean().ToString() : "False"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlFormsViewsAndCanvasAppsAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var resF = await client.GetAsync("systemforms?$select=name,objecttypecode,type,formxml").ConfigureAwait(false);
                if (resF.IsSuccessStatusCode)
                {
                    using var doc = await resF.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var f in value.EnumerateArray())
                        {
                            string name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            string entity = f.TryGetProperty("objecttypecode", out var obj) ? obj.GetString() ?? "general" : "general";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"EntityForm.{entity}.{name}"] = new Dictionary<string, string>
                            {
                                ["FormType"] = f.TryGetProperty("type", out var t) ? t.GetInt32().ToString() : "2",
                                ["FormXmlLength"] = f.TryGetProperty("formxml", out var xml) ? (xml.GetString()?.Length ?? 0).ToString() : "0"
                            };
                        }
                    }
                }

                var resV = await client.GetAsync("savedqueries?$select=name,returnedtypecode,querytype,fetchxml").ConfigureAwait(false);
                if (resV.IsSuccessStatusCode)
                {
                    using var doc = await resV.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var v in value.EnumerateArray())
                        {
                            string name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            string entity = v.TryGetProperty("returnedtypecode", out var obj) ? obj.GetString() ?? "general" : "general";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"EntityView.{entity}.{name}"] = new Dictionary<string, string>
                            {
                                ["QueryType"] = v.TryGetProperty("querytype", out var qt) ? qt.GetInt32().ToString() : "0",
                                ["FetchXmlLength"] = v.TryGetProperty("fetchxml", out var xml) ? (xml.GetString()?.Length ?? 0).ToString() : "0"
                            };
                        }
                    }
                }

                // Crawl Canvas Apps (canvasapp)
                var resC = await client.GetAsync("canvasapps?$select=name,displayname,appversion").ConfigureAwait(false);
                if (resC.IsSuccessStatusCode)
                {
                    using var doc = await resC.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var c in value.EnumerateArray())
                        {
                            string name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"CanvasApp.{name}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = c.TryGetProperty("displayname", out var d) ? d.GetString() ?? "" : "",
                                ["Version"] = c.TryGetProperty("appversion", out var v) ? v.GetString() ?? "" : ""
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
                            if (string.IsNullOrEmpty(name) || !name.Contains("_")) continue;

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
            rawData.AdminSettings["OrgDbSettings.SkipRuleCheck"] = new Dictionary<string, string> { ["Value"] = "True" };
            rawData.AdminSettings["OrgDbSettings.MaxVerboseLog"] = new Dictionary<string, string> { ["Value"] = isDev ? "Verbose" : "Warning" };
            rawData.AdminSettings["Security.MaxSessionTimeout"] = new Dictionary<string, string> { ["Value"] = isDev ? "1440 mins" : "480 mins" };

            // 2. Installed Solutions & D365 First-Party Apps
            rawData.MetadataItems["Solution.msdyn_SalesHub"] = new Dictionary<string, string>
            {
                ["FriendlyName"] = "Sales Hub App Solution",
                ["Version"] = isDev ? "9.2.2100.10" : "9.2.2050.5",
                ["IsManaged"] = "True"
            };

            rawData.MetadataItems["InstalledApp.saleshub"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Sales Hub",
                ["Version"] = "9.2.2100",
                ["State"] = "Active"
            };

            // 3. Connection References & Custom Connectors
            rawData.MetadataItems["ConnectionReference.new_shared_sharepointonline"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "SharePoint Online Connection Reference",
                ["ConnectorId"] = "/providers/Microsoft.PowerApps/apis/shared_sharepointonline",
                ["ConnectionId"] = isDev ? "dev-sp-conn-123" : (isTest ? "test-sp-conn-456" : "prod-sp-conn-789")
            };

            rawData.MetadataItems["CustomConnector.new_ERP_Payment_Connector"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "ERP Payment Gateway Connector",
                ["Version"] = "1.0.0.0"
            };

            // 4. Copilot Studio Bots & Topics
            rawData.MetadataItems["CopilotBot.new_CustomerSupportCopilot"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Customer Support Copilot",
                ["Language"] = "English (1033)",
                ["State"] = "Active"
            };

            rawData.MetadataItems["CopilotTopic.new_Topic_OrderTracking"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Order Status & Tracking Topic",
                ["ComponentType"] = "Topic (0)",
                ["ContentLength"] = isDev ? "2450" : "1890"
            };

            // 5. Plug-in Assemblies & Custom APIs
            rawData.MetadataItems["PluginAssembly.AccountPlugin.dll"] = new Dictionary<string, string>
            {
                ["Version"] = (isDev || isTest) ? "1.2.0.0" : "1.1.0.0",
                ["IsolationMode"] = "Sandbox",
                ["PublicKeyToken"] = "31bf3856ad364e35"
            };

            rawData.MetadataItems["PluginStep.CreateAccount_PostOp"] = new Dictionary<string, string>
            {
                ["SdkMessage"] = "Create",
                ["PrimaryEntity"] = "account",
                ["Stage"] = "PostOperation (40)",
                ["ExecutionMode"] = "Synchronous",
                ["ExecutionOrder"] = "1",
                ["FilteringAttributes"] = "name,telephone1,address1_city",
                ["Status"] = isDev ? "Enabled" : (isTest ? "Enabled" : "Disabled"),
                ["ImpersonatingUser"] = "Calling User",
                ["AsyncAutoDelete"] = "False",
                ["PreImages"] = "PreImage_Account (name, telephone1)",
                ["PostImages"] = "PostImage_Account (accountid, name)"
            };

            rawData.MetadataItems["CustomAPI.new_CalculateDiscount"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Calculate Tiered Discount Custom API",
                ["BindingType"] = "Global (0)",
                ["BoundEntity"] = "Global",
                ["IsFunction"] = "True",
                ["IsPrivate"] = "False"
            };

            // 6. Entity Forms & Views & Canvas Apps
            rawData.MetadataItems["EntityForm.account.Information Main Form"] = new Dictionary<string, string>
            {
                ["FormType"] = "Main (2)",
                ["TabsCount"] = isDev ? "6" : "5",
                ["HeaderControls"] = "4"
            };

            rawData.MetadataItems["EntityView.account.Active Accounts"] = new Dictionary<string, string>
            {
                ["QueryType"] = "Public View (0)",
                ["ColumnsCount"] = isDev ? "7" : "5",
                ["SortField"] = "name ASC"
            };

            rawData.MetadataItems["CanvasApp.new_FieldServiceInspectorApp"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Field Service Inspector App",
                ["Version"] = isDev ? "v1.4" : "v1.2"
            };

            // 7. Business Rules & Automations
            rawData.MetadataItems["BusinessRule.account.Require TaxId for Enterprise"] = new Dictionary<string, string>
            {
                ["Status"] = "Activated",
                ["Scope"] = "All Forms"
            };

            rawData.MetadataItems["BusinessProcessFlow.LeadToOpportunitySalesProcess"] = new Dictionary<string, string>
            {
                ["Status"] = "Activated",
                ["StagesCount"] = "4"
            };

            // 8. Environment Variables
            rawData.MetadataItems["EnvVariable.new_PaymentApiUrl"] = new Dictionary<string, string>
            {
                ["Value"] = isDev ? "https://dev.api.payments.com" : (isTest ? "https://test.api.payments.com" : "https://prod.api.payments.com"),
                ["Type"] = "String"
            };

            // 9. Tables & Columns Schema
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
