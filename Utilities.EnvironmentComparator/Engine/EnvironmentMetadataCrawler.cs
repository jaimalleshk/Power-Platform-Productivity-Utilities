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
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[SIMULATION] Crawling Dashboards, Env Variables, Solutions, PCF Controls, & D365 components for {envName}...", PercentComplete = 50 });
                GenerateSimulationData(envName, rawData, scope);
                progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[SIMULATION] Completed full metadata crawl for {envName}.", PercentComplete = 100 });
                return rawData;
            }

            using var httpClient = _connectionFactory.CreateHttpClient(profile);

            // 1. Crawl Admin Settings & Environment Variables (Root 1)
            progress?.Report(new ProgressUpdate { Stage = "Admin Crawl", Message = $"[{envName}] Fetching Organization Settings & Environment Variable Values...", PercentComplete = 10 });
            await CrawlAdminSettingsAsync(httpClient, envName, rawData).ConfigureAwait(false);
            await CrawlEnvVariablesAsync(httpClient, rawData).ConfigureAwait(false);

            // 2. Crawl First-Party & Custom Solutions
            progress?.Report(new ProgressUpdate { Stage = "Solutions Crawl", Message = $"[{envName}] Fetching Solutions and Version Inventories...", PercentComplete = 20 });
            await CrawlSolutionsAndAppsAsync(httpClient, rawData).ConfigureAwait(false);

            // 3. Crawl System & Interactive Dashboards
            progress?.Report(new ProgressUpdate { Stage = "Dashboards Crawl", Message = $"[{envName}] Fetching System Dashboards & User Dashboards...", PercentComplete = 30 });
            await CrawlDashboardsAsync(httpClient, rawData).ConfigureAwait(false);

            // 4. Crawl PCF Controls & Custom Control Resources
            progress?.Report(new ProgressUpdate { Stage = "PCF Crawl", Message = $"[{envName}] Fetching PCF Controls (customcontrols)...", PercentComplete = 40 });
            await CrawlPcfControlsAsync(httpClient, rawData).ConfigureAwait(false);

            // 5. Crawl SiteMaps
            progress?.Report(new ProgressUpdate { Stage = "SiteMaps Crawl", Message = $"[{envName}] Fetching Site Maps & Navigation Menus...", PercentComplete = 50 });
            await CrawlSiteMapsAsync(httpClient, rawData).ConfigureAwait(false);

            // 6. Crawl Field Security Profiles & Permissions
            progress?.Report(new ProgressUpdate { Stage = "Security Crawl", Message = $"[{envName}] Fetching Field Security Profiles...", PercentComplete = 60 });
            await CrawlFieldSecurityProfilesAsync(httpClient, rawData).ConfigureAwait(false);

            // 7. Crawl Connection References & Custom Connectors
            progress?.Report(new ProgressUpdate { Stage = "Connectors Crawl", Message = $"[{envName}] Fetching Connection References & Custom Connectors...", PercentComplete = 70 });
            await CrawlConnectionReferencesAndConnectorsAsync(httpClient, rawData).ConfigureAwait(false);

            // 8. Crawl Copilot Studio Bots & Topics
            progress?.Report(new ProgressUpdate { Stage = "Copilot Studio Crawl", Message = $"[{envName}] Fetching Copilot Studio Bots & Topics...", PercentComplete = 75 });
            await CrawlCopilotStudioAsync(httpClient, rawData).ConfigureAwait(false);

            // 9. Crawl Plug-in Assemblies, 100% Step Registration Attributes, & Custom APIs
            progress?.Report(new ProgressUpdate { Stage = "Plugins Crawl", Message = $"[{envName}] Fetching Plug-in Assemblies, Steps, & Custom APIs...", PercentComplete = 85 });
            await CrawlPluginsAndCustomApisAsync(httpClient, rawData).ConfigureAwait(false);

            // 10. Crawl Forms, Views, Canvas Apps, Custom Pages, & Tables (OOB & Custom)
            progress?.Report(new ProgressUpdate { Stage = "Tables Crawl", Message = $"[{envName}] Fetching Forms, Views, Columns, & OOB/Custom Tables...", PercentComplete = 95 });
            await CrawlFormsViewsAndCanvasAppsAsync(httpClient, rawData).ConfigureAwait(false);
            await CrawlTablesAsync(httpClient, rawData).ConfigureAwait(false);

            // Execute Extensibility Providers
            await ComparisonProviderRegistry.ExecuteAllProvidersAsync(httpClient, envName, rawData, scope).ConfigureAwait(false);

            progress?.Report(new ProgressUpdate { Stage = "Metadata Crawl", Message = $"[{envName}] Completed full D365 non-transactional metadata crawl.", PercentComplete = 100 });
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

        private async Task CrawlEnvVariablesAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var resDef = await client.GetAsync("environmentvariabledefinitions?$select=schemaname,displayname,defaultvalue,type").ConfigureAwait(false);
                if (resDef.IsSuccessStatusCode)
                {
                    using var doc = await resDef.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var def in value.EnumerateArray())
                        {
                            string schema = def.GetProperty("schemaname").GetString() ?? "";
                            if (string.IsNullOrEmpty(schema)) continue;

                            string defaultVal = def.TryGetProperty("defaultvalue", out var dv) ? dv.GetString() ?? "" : "";
                            string typeStr = def.TryGetProperty("type", out var t) ? t.GetInt32().ToString() : "0";

                            // Root 1 Entry: Environment Variable Definitions & Values
                            rawData.AdminSettings[$"EnvVariable.{schema}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = def.TryGetProperty("displayname", out var dn) ? dn.GetString() ?? "" : "",
                                ["DefaultValue"] = defaultVal,
                                ["Type"] = typeStr,
                                ["Value"] = defaultVal
                            };

                            // Root 2 Entry: Environment Variable Metadata Node
                            rawData.MetadataItems[$"EnvVariable.{schema}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = def.TryGetProperty("displayname", out var dn2) ? dn2.GetString() ?? "" : "",
                                ["DefaultValue"] = defaultVal,
                                ["Type"] = typeStr,
                                ["Value"] = defaultVal
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlDashboardsAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var resDash = await client.GetAsync("systemforms?$filter=type eq 0&$select=name,description,formxml").ConfigureAwait(false);
                if (resDash.IsSuccessStatusCode)
                {
                    using var doc = await resDash.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var dash in value.EnumerateArray())
                        {
                            string name = dash.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"Dashboard.{name}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = name,
                                ["Description"] = dash.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                                ["XmlLength"] = dash.TryGetProperty("formxml", out var xml) ? (xml.GetString()?.Length ?? 0).ToString() : "0"
                            };
                        }
                    }
                }
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

        private async Task CrawlPcfControlsAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var res = await client.GetAsync("customcontrols?$select=name,version,manifest").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var ctrl in value.EnumerateArray())
                        {
                            string name = ctrl.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"PcfControl.{name}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = name,
                                ["Version"] = ctrl.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                                ["ManifestLength"] = ctrl.TryGetProperty("manifest", out var m) ? (m.GetString()?.Length ?? 0).ToString() : "0"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlSiteMapsAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var res = await client.GetAsync("sitemaps?$select=sitemapname,sitemapxml").ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var sm in value.EnumerateArray())
                        {
                            string name = sm.TryGetProperty("sitemapname", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"SiteMap.{name}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = name,
                                ["XmlLength"] = sm.TryGetProperty("sitemapxml", out var xml) ? (xml.GetString()?.Length ?? 0).ToString() : "0"
                            };
                        }
                    }
                }
            }
            catch { }
        }

        private async Task CrawlFieldSecurityProfilesAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
                var resFSP = await client.GetAsync("fieldsecurityprofiles?$select=name,description").ConfigureAwait(false);
                if (resFSP.IsSuccessStatusCode)
                {
                    using var doc = await resFSP.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var fsp in value.EnumerateArray())
                        {
                            string name = fsp.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            rawData.MetadataItems[$"FieldSecurityProfile.{name}"] = new Dictionary<string, string>
                            {
                                ["Description"] = fsp.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                                ["Status"] = "Active"
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
            }
            catch { }
        }

        private async Task CrawlCopilotStudioAsync(HttpClient client, RawEnvData rawData)
        {
            try
            {
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
                                ["IsolationMode"] = asm.TryGetProperty("isolationmode", out var iso) ? iso.GetInt32().ToString() : "1"
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
                var resF = await client.GetAsync("systemforms?$filter=type ne 0&$select=name,objecttypecode,type,formxml").ConfigureAwait(false);
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

                var resC = await client.GetAsync("canvasapps?$select=name,displayname,appversion,canvasapptype").ConfigureAwait(false);
                if (resC.IsSuccessStatusCode)
                {
                    using var doc = await resC.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var c in value.EnumerateArray())
                        {
                            string name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(name)) continue;

                            int appType = c.TryGetProperty("canvasapptype", out var cat) ? cat.GetInt32() : 0;
                            string itemPrefix = appType == 1 ? "CustomPage." : "CanvasApp.";

                            rawData.MetadataItems[$"{itemPrefix}{name}"] = new Dictionary<string, string>
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
                            bool isCustom = ent.TryGetProperty("IsCustomEntity", out var ic) && ic.GetBoolean();
                            string tableType = isCustom ? "CustomTable" : "OOBTable";

                            rawData.MetadataItems[$"{tableType}.{name}"] = new Dictionary<string, string>
                            {
                                ["DisplayName"] = name,
                                ["IsCustom"] = isCustom.ToString(),
                                ["IsCustomizable"] = ent.TryGetProperty("IsCustomizable", out var icz) && icz.TryGetProperty("Value", out var v) ? v.GetBoolean().ToString() : "true"
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

            // 1. Admin Settings & Environment Variables (Root 1)
            rawData.AdminSettings["OrgDbSettings.SkipRuleCheck"] = new Dictionary<string, string> { ["Value"] = "True" };
            rawData.AdminSettings["AutoNumber.account.accountnumber"] = new Dictionary<string, string> { ["Format"] = isDev ? "ACC-{SEQ:6}" : "ACC-{SEQ:5}" };

            rawData.AdminSettings["EnvVariable.new_PaymentApiUrl"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Payment API Endpoint URL",
                ["DefaultValue"] = "https://api.payments.com",
                ["Type"] = "String",
                ["Value"] = isDev ? "https://dev.api.payments.com" : (isTest ? "https://test.api.payments.com" : "https://prod.api.payments.com")
            };

            // 2. Metadata Items (Root 2)
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

            // System Dashboards
            rawData.MetadataItems["Dashboard.Sales Performance Dashboard"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Sales Performance Dashboard",
                ["Description"] = "Executive overview of pipeline revenue and active deals",
                ["XmlLength"] = isDev ? "8450" : "7800"
            };

            // PCF Controls & Custom Pages
            rawData.MetadataItems["PcfControl.Msdyn.GridControl"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Fluent Power Apps Grid PCF Control",
                ["Version"] = isDev ? "1.4.0" : "1.2.0"
            };

            rawData.MetadataItems["CustomPage.new_CustomerOverviewPage"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Customer 360 Overview Custom Page",
                ["Version"] = isDev ? "1.2" : "1.0"
            };

            // SiteMaps
            rawData.MetadataItems["SiteMap.msdyn_SalesSiteMap"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Sales App Site Map Navigation Menu",
                ["XmlLength"] = isDev ? "12450" : "10200"
            };

            // Plug-in Assemblies & ALL PRT Registration Step Config Attributes
            rawData.MetadataItems["PluginAssembly.AccountPlugin.dll"] = new Dictionary<string, string>
            {
                ["Version"] = (isDev || isTest) ? "1.2.0.0" : "1.1.0.0",
                ["IsolationMode"] = "Sandbox"
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
                ["SupportedDeployment"] = "Server-Only (0)",
                ["UnsecureConfig"] = "<config><setting>val</setting></config>",
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

            // Tables (OOB vs Custom) & Forms/Views
            rawData.MetadataItems["OOBTable.account"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Account",
                ["IsCustom"] = "False",
                ["IsCustomizable"] = "True"
            };

            rawData.MetadataItems["CustomTable.new_loyaltyprogram"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Loyalty Program",
                ["IsCustom"] = "True",
                ["IsCustomizable"] = "True"
            };

            rawData.MetadataItems["EntityForm.account.Information Main Form"] = new Dictionary<string, string>
            {
                ["FormType"] = "Main (2)",
                ["TabsCount"] = isDev ? "6" : "5"
            };

            rawData.MetadataItems["EntityView.account.Active Accounts"] = new Dictionary<string, string>
            {
                ["QueryType"] = "Public View (0)",
                ["ColumnsCount"] = isDev ? "7" : "5"
            };

            rawData.MetadataItems["EnvVariable.new_PaymentApiUrl"] = new Dictionary<string, string>
            {
                ["DisplayName"] = "Payment API Endpoint URL",
                ["DefaultValue"] = "https://api.payments.com",
                ["Type"] = "String",
                ["Value"] = isDev ? "https://dev.api.payments.com" : (isTest ? "https://test.api.payments.com" : "https://prod.api.payments.com")
            };
        }
    }
}
