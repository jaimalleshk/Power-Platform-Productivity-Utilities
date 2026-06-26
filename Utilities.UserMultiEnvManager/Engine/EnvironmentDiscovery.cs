using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Authentication;
using PowerPlatform.ProductivityEngine.Core.Connections;
using Utilities.UserMultiEnvManager.Models;

namespace Utilities.UserMultiEnvManager.Engine
{
    public class EnvironmentDiscovery
    {
        private readonly IAuthenticationProvider _authProvider;

        public EnvironmentDiscovery(IAuthenticationProvider? authProvider = null)
        {
            _authProvider = authProvider ?? new MsalAuthenticationProvider();
        }

        public async Task<List<InstanceDto>> DiscoverEnvironmentsAsync(ConnectionProfile profile, Action<string>? logCallback = null)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            logCallback?.Invoke("Initiating Dataverse Global Discovery Service query...");

            if (profile.EnvironmentUrl != null && profile.EnvironmentUrl.Contains("simulation-env.crm.dynamics.com"))
            {
                logCallback?.Invoke("[SIMULATION] Resolving mock environments for the tenant...");
                return new List<InstanceDto>
                {
                    new InstanceDto { Id = Guid.NewGuid(), UniqueName = "contoso-dev", FriendlyName = "Contoso Development", Url = "https://contoso-dev.crm.dynamics.com", ApiUrl = "https://contoso-dev.crm.dynamics.com/api/data/v9.2" },
                    new InstanceDto { Id = Guid.NewGuid(), UniqueName = "contoso-test", FriendlyName = "Contoso Testing", Url = "https://contoso-test.crm.dynamics.com", ApiUrl = "https://contoso-test.crm.dynamics.com/api/data/v9.2" },
                    new InstanceDto { Id = Guid.NewGuid(), UniqueName = "contoso-prod", FriendlyName = "Contoso Production", Url = "https://contoso-prod.crm.dynamics.com", ApiUrl = "https://contoso-prod.crm.dynamics.com/api/data/v9.2" }
                };
            }

            // If a connection string is provided but we don't have interactive auth or secret details,
            // we will try to discover, but if it fails we will fall back to using the profile's own EnvironmentUrl.
            try
            {
                // Clone the profile and direct it to the Global Discovery Service endpoint
                var discoveryProfile = new ConnectionProfile
                {
                    ConnectionString = profile.ConnectionString,
                    EnvironmentUrl = "https://globaldisco.crm.dynamics.com",
                    TenantId = profile.TenantId,
                    ClientId = profile.ClientId,
                    ClientSecret = profile.ClientSecret,
                    ClientCertificateThumbprint = profile.ClientCertificateThumbprint,
                    Username = profile.Username,
                    Password = profile.Password,
                    UseInteractiveAuth = profile.UseInteractiveAuth,
                    RedirectUri = profile.RedirectUri,
                    LoginHint = profile.LoginHint,
                    TimeoutSeconds = profile.TimeoutSeconds
                };

                string token = await _authProvider.GetAccessTokenAsync(discoveryProfile).ConfigureAwait(false);

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(profile.TimeoutSeconds);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                string url = "https://globaldisco.crm.dynamics.com/api/discovery/v9.0/Instances";
                var response = await client.GetAsync(url).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var discoResult = JsonSerializer.Deserialize<DiscoveryResponse>(content);
                    if (discoResult?.Value != null && discoResult.Value.Count > 0)
                    {
                        logCallback?.Invoke($"Successfully discovered {discoResult.Value.Count} environments from the tenant.");
                        return discoResult.Value;
                    }
                }
                
                logCallback?.Invoke($"Global Discovery endpoint returned status: {response.StatusCode}. Falling back to connection profile environment.");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Global Discovery failed: {ex.Message}. Falling back to single environment profile.");
            }

            // Fallback: If global discovery fails, return the environment URL defined in the connection profile
            if (!string.IsNullOrWhiteSpace(profile.EnvironmentUrl))
            {
                string friendlyName = ParseFriendlyName(profile.EnvironmentUrl);
                string uniqueName = ParseUniqueName(profile.EnvironmentUrl);
                logCallback?.Invoke($"Resolved single environment scope: {profile.EnvironmentUrl}");
                return new List<InstanceDto>
                {
                    new InstanceDto
                    {
                        Id = Guid.Empty,
                        FriendlyName = friendlyName,
                        UniqueName = uniqueName,
                        Url = profile.EnvironmentUrl,
                        ApiUrl = profile.EnvironmentUrl.TrimEnd('/') + "/api/data/v9.2"
                    }
                };
            }

            return new List<InstanceDto>();
        }

        private string ParseFriendlyName(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host.Split('.')[0];
            }
            catch
            {
                return url;
            }
        }

        private string ParseUniqueName(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host.Split('.')[0];
            }
            catch
            {
                return url;
            }
        }
    }
}
