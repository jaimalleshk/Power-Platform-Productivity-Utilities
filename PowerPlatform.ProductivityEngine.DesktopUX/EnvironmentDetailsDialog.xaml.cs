using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using PowerPlatform.ProductivityEngine.Core.Authentication;
using PowerPlatform.ProductivityEngine.Core.Connections;
using PowerPlatform.ProductivityEngine.Core.Storage;
using PowerPlatform.ProductivityEngine.DesktopUX.ViewModels;

namespace PowerPlatform.ProductivityEngine.DesktopUX
{
    public partial class EnvironmentDetailsDialog : Window
    {
        private readonly SelectableEnv _env;
        private readonly string _userEmail;
        private ObservableCollection<KeyValueRow> UserDetailsRows { get; } = new();
        private ObservableCollection<KeyValueRow> OrgDetailsRows { get; } = new();
        private ObservableCollection<string> AssignedRoles { get; } = new();

        public EnvironmentDetailsDialog(SelectableEnv env, string userEmail)
        {
            InitializeComponent();
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _userEmail = userEmail;

            TxtTitle.Text = $"🌐 Details & Security Roles: {_env.RawName}";
            GridUserDetails.ItemsSource = UserDetailsRows;
            GridOrgDetails.ItemsSource = OrgDetailsRows;
            RolesItemsControl.ItemsSource = AssignedRoles;

            LoadDetailsFromCache();
        }

        private void LoadDetailsFromCache()
        {
            var cached = EnvironmentDetailsCache.GetCachedDetails(_env.Url);
            if (cached != null)
            {
                DisplayDetails(cached);
                TxtStatus.Text = $"Loaded from Local SQLite Cache (Refreshed: {cached.LastRefreshed.ToLocalTime():yyyy-MM-dd HH:mm:ss})";
            }
            else
            {
                TxtStatus.Text = "No local SQLite cache found. Querying live Dataverse Web API...";
                _ = RefreshDetailsAsync();
            }
        }

        private void DisplayDetails(EnvironmentDetailsModel model)
        {
            AssignedRoles.Clear();
            if (model.AssignedRoles != null && model.AssignedRoles.Count > 0)
            {
                foreach (var role in model.AssignedRoles)
                {
                    AssignedRoles.Add(role);
                }
            }
            else
            {
                AssignedRoles.Add("Basic User");
            }
            TxtRoleCount.Text = $"Count: {AssignedRoles.Count}";

            UserDetailsRows.Clear();
            UserDetailsRows.Add(new KeyValueRow { Key = "Full Name", Value = model.FullName });
            UserDetailsRows.Add(new KeyValueRow { Key = "Email / UPN", Value = model.Email });
            UserDetailsRows.Add(new KeyValueRow { Key = "SystemUser ID", Value = model.SystemUserId });
            UserDetailsRows.Add(new KeyValueRow { Key = "Business Unit", Value = model.BusinessUnit });
            UserDetailsRows.Add(new KeyValueRow { Key = "Access Mode", Value = model.AccessMode });
            UserDetailsRows.Add(new KeyValueRow { Key = "Is System Admin", Value = model.IsAdmin ? "YES (System Administrator)" : "NO" });

            OrgDetailsRows.Clear();
            OrgDetailsRows.Add(new KeyValueRow { Key = "Environment Name", Value = model.EnvironmentName });
            OrgDetailsRows.Add(new KeyValueRow { Key = "Environment URL", Value = model.EnvironmentUrl });

            if (model.OrgMetadata != null)
            {
                foreach (var kv in model.OrgMetadata)
                {
                    OrgDetailsRows.Add(new KeyValueRow { Key = kv.Key, Value = kv.Value });
                }
            }
            OrgDetailsRows.Add(new KeyValueRow { Key = "Last Refreshed", Value = model.LastRefreshed.ToLocalTime().ToString("g") });
        }

        private async Task RefreshDetailsAsync()
        {
            TxtStatus.Text = "Connecting to Dataverse Web API & retrieving security roles...";

            var model = new EnvironmentDetailsModel
            {
                EnvironmentUrl = _env.Url,
                EnvironmentName = _env.RawName,
                Email = _userEmail
            };

            try
            {
                var authProvider = new MsalAuthenticationProvider(username: _userEmail);
                var profile = new ConnectionProfile
                {
                    EnvironmentUrl = _env.Url,
                    Username = _userEmail,
                    UseInteractiveAuth = true
                };

                string token = await authProvider.GetAccessTokenAsync(profile).ConfigureAwait(false);

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // 1. WhoAmI
                var whoRes = await client.GetAsync($"{_env.Url.TrimEnd('/')}/api/data/v9.2/WhoAmI").ConfigureAwait(false);
                if (whoRes.IsSuccessStatusCode)
                {
                    using var doc = await whoRes.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("UserId", out var uidProp))
                    {
                        model.SystemUserId = uidProp.GetString() ?? "";
                    }
                }

                // 2. Fetch User & Security Roles
                if (!string.IsNullOrEmpty(model.SystemUserId))
                {
                    var rolesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var userRes = await client.GetAsync($"{_env.Url.TrimEnd('/')}/api/data/v9.2/systemusers({model.SystemUserId})?$select=fullname,internalemailaddress,accessmode&$expand=businessunitid($select=name),systemuserroles_association($select=name),user_roles_association($select=name)").ConfigureAwait(false);
                    if (userRes.IsSuccessStatusCode)
                    {
                        using var uDoc = await userRes.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                        if (uDoc != null)
                        {
                            var root = uDoc.RootElement;
                            if (root.TryGetProperty("fullname", out var fn)) model.FullName = fn.GetString() ?? "";
                            if (root.TryGetProperty("internalemailaddress", out var em) && string.IsNullOrEmpty(model.Email)) model.Email = em.GetString() ?? "";
                            if (root.TryGetProperty("accessmode", out var am)) model.AccessMode = am.GetRawText() == "0" ? "Read-Write" : "Administrative/Non-Interactive";

                            if (root.TryGetProperty("businessunitid", out var buObj) && buObj.TryGetProperty("name", out var buName))
                            {
                                model.BusinessUnit = buName.GetString() ?? "";
                            }

                            if (root.TryGetProperty("systemuserroles_association", out var sysRoles) && sysRoles.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var r in sysRoles.EnumerateArray())
                                {
                                    if (r.TryGetProperty("name", out var rnp))
                                    {
                                        string rName = rnp.GetString() ?? "";
                                        if (!string.IsNullOrWhiteSpace(rName)) rolesList.Add(rName);
                                    }
                                }
                            }

                            if (root.TryGetProperty("user_roles_association", out var usrRoles) && usrRoles.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var r in usrRoles.EnumerateArray())
                                {
                                    if (r.TryGetProperty("name", out var rnp))
                                    {
                                        string rName = rnp.GetString() ?? "";
                                        if (!string.IsNullOrWhiteSpace(rName)) rolesList.Add(rName);
                                    }
                                }
                            }
                        }
                    }

                    // Direct roles collection fallback
                    var directRes = await client.GetAsync($"{_env.Url.TrimEnd('/')}/api/data/v9.2/systemusers({model.SystemUserId})/systemuserroles_association?$select=name").ConfigureAwait(false);
                    if (directRes.IsSuccessStatusCode)
                    {
                        using var dDoc = await directRes.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                        if (dDoc != null && dDoc.RootElement.TryGetProperty("value", out var dArr) && dArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var r in dArr.EnumerateArray())
                            {
                                if (r.TryGetProperty("name", out var rnp))
                                {
                                    string rName = rnp.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(rName)) rolesList.Add(rName);
                                }
                            }
                        }
                    }

                    // Teams inherited roles
                    var teamRes = await client.GetAsync($"{_env.Url.TrimEnd('/')}/api/data/v9.2/systemusers({model.SystemUserId})/teammembership_association?$select=teamid,name&$expand=teamroles_association($select=name)").ConfigureAwait(false);
                    if (teamRes.IsSuccessStatusCode)
                    {
                        using var tDoc = await teamRes.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                        if (tDoc != null && tDoc.RootElement.TryGetProperty("value", out var tArr) && tArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var t in tArr.EnumerateArray())
                            {
                                if (t.TryGetProperty("teamroles_association", out var trArr) && trArr.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var tr in trArr.EnumerateArray())
                                    {
                                        if (tr.TryGetProperty("name", out var trnp))
                                        {
                                            string rName = trnp.GetString() ?? "";
                                            if (!string.IsNullOrWhiteSpace(rName)) rolesList.Add($"[Team] {rName}");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    model.AssignedRoles = rolesList.OrderBy(r => r).ToList();
                    model.IsAdmin = rolesList.Any(r => r.Contains("System Administrator", StringComparison.OrdinalIgnoreCase));
                }

                // 3. Environment Org details
                var orgRes = await client.GetAsync($"{_env.Url.TrimEnd('/')}/api/data/v9.2/organizations?$select=name,crmversion,organizationtype").ConfigureAwait(false);
                if (orgRes.IsSuccessStatusCode)
                {
                    using var oDoc = await orgRes.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (oDoc != null && oDoc.RootElement.TryGetProperty("value", out var oArr) && oArr.ValueKind == JsonValueKind.Array && oArr.GetArrayLength() > 0)
                    {
                        var firstOrg = oArr[0];
                        if (firstOrg.TryGetProperty("crmversion", out var ver)) model.OrgMetadata["Dataverse Version"] = ver.GetString() ?? "";
                        if (firstOrg.TryGetProperty("organizationtype", out var ot)) model.OrgMetadata["Organization Type"] = ot.GetRawText();
                    }
                }

                model.OrgMetadata["Web API Endpoint"] = $"{_env.Url.TrimEnd('/')}/api/data/v9.2/";
                model.LastRefreshed = DateTimeOffset.UtcNow;

                // Save to local SQLite cache
                EnvironmentDetailsCache.SaveDetails(model);

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    DisplayDetails(model);
                    _env.IsAdmin = model.IsAdmin;
                    TxtStatus.Text = $"Successfully refreshed & saved to SQLite Cache! ({DateTime.Now:T})";
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = $"Error refreshing Web API: {ex.Message}";
                });
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await RefreshDetailsAsync();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
