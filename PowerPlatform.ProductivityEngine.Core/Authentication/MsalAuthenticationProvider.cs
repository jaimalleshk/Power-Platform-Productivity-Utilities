using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.PowerPlatform.Dataverse.Client;
using PowerPlatform.ProductivityEngine.Core.Connections;

namespace PowerPlatform.ProductivityEngine.Core.Authentication
{
    public class DiscoveredTenantEnv
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string EnvironmentId { get; set; } = string.Empty;
    }

    public class MsalAuthenticationProvider : IAuthenticationProvider
    {
        // First-party Azure PowerShell / Azure CLI Client ID (51f81489-12ee-4a9e-aaae-a2591f45987d)
        // Broadly pre-consented across enterprise Microsoft Entra ID tenants (avoiding AADSTS65001 unconsented errors)
        public const string PowerPlatformFirstPartyClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

        private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)> TokenCache = 
            new ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)>();

        private static (string Token, DateTimeOffset ExpiresOn)? SharedSsoToken;
        private static readonly SemaphoreSlim AuthSemaphore = new SemaphoreSlim(1, 1);

        public string? PreferredUsername { get; }
        public string? TenantId { get; }

        public MsalAuthenticationProvider(string? username = null, string? tenantId = null)
        {
            PreferredUsername = username;
            TenantId = tenantId;
        }

        public void SetSharedSsoToken(string token, DateTimeOffset expiresOn)
        {
            SharedSsoToken = (token, expiresOn);
        }

        public static async Task<string> AutoDiscoverTenantIdAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || !username.Contains('@'))
                return "organizations";

            string domain = username.Split('@')[1].Trim();
            try
            {
                using var client = new HttpClient();
                var url = $"https://login.microsoftonline.com/{domain}/v2.0/.well-known/openid-configuration";
                var res = await client.GetAsync(url).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                    if (doc != null && doc.RootElement.TryGetProperty("token_endpoint", out var tokenEp))
                    {
                        string epStr = tokenEp.GetString() ?? "";
                        var parts = epStr.Split('/');
                        if (parts.Length >= 4 && Guid.TryParse(parts[3], out var tenantGuid))
                        {
                            return tenantGuid.ToString();
                        }
                    }
                }
            }
            catch { }

            return domain; // Fallback to domain name which MSAL resolves automatically
        }

        public async Task<List<DiscoveredTenantEnv>> DiscoverEnvironmentsAsync()
        {
            var discoveredEnvs = new List<DiscoveredTenantEnv>();

            string effectiveTenant = string.IsNullOrWhiteSpace(TenantId)
                ? await AutoDiscoverTenantIdAsync(PreferredUsername ?? "").ConfigureAwait(false)
                : TenantId;

            string authority = $"https://login.microsoftonline.com/{(string.IsNullOrWhiteSpace(effectiveTenant) ? "organizations" : effectiveTenant)}";

            var pca = PublicClientApplicationBuilder.Create(PowerPlatformFirstPartyClientId)
                .WithAuthority(authority)
                .WithRedirectUri("http://localhost")
                .Build();

            // Primary scope: .default
            string[] scopes = new[] { "https://globaldisco.crm.dynamics.com/.default" };

            AuthenticationResult authResult;
            try
            {
                // Force SelectAccount prompt so MSAL popup allows account picking without auto-selecting local Windows WAM hint
                var acquireTokenBuilder = pca.AcquireTokenInteractive(scopes)
                    .WithPrompt(Prompt.SelectAccount);

                if (!string.IsNullOrWhiteSpace(PreferredUsername))
                {
                    acquireTokenBuilder = acquireTokenBuilder.WithLoginHint(PreferredUsername);
                }

                authResult = await acquireTokenBuilder.ExecuteAsync().ConfigureAwait(false);
            }
            catch (MsalException)
            {
                // Fallback attempt with user_impersonation scope
                scopes = new[] { "https://globaldisco.crm.dynamics.com/user_impersonation" };
                var acquireTokenBuilder = pca.AcquireTokenInteractive(scopes)
                    .WithPrompt(Prompt.SelectAccount);

                if (!string.IsNullOrWhiteSpace(PreferredUsername))
                {
                    acquireTokenBuilder = acquireTokenBuilder.WithLoginHint(PreferredUsername);
                }

                authResult = await acquireTokenBuilder.ExecuteAsync().ConfigureAwait(false);
            }

            string token = authResult.AccessToken;
            SetSharedSsoToken(token, authResult.ExpiresOn);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.GetAsync("https://globaldisco.crm.dynamics.com/api/discovery/v9.2/Instances").ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                if (doc != null && doc.RootElement.TryGetProperty("value", out var value))
                {
                    foreach (var inst in value.EnumerateArray())
                    {
                        string name = inst.TryGetProperty("FriendlyName", out var fn) ? fn.GetString() ?? "" : "";
                        string url = inst.TryGetProperty("ApiUrl", out var u) ? u.GetString() ?? "" : "";
                        string id = inst.TryGetProperty("Id", out var i) ? i.GetString() ?? "" : "";

                        if (!string.IsNullOrEmpty(url))
                        {
                            discoveredEnvs.Add(new DiscoveredTenantEnv
                            {
                                Name = string.IsNullOrEmpty(name) ? url : name,
                                Url = url,
                                EnvironmentId = id
                            });
                        }
                    }
                }
            }

            return discoveredEnvs;
        }

        public async Task<string> GetAccessTokenAsync(ConnectionProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            // Check if Single Sign-On (SSO) shared token is active and valid
            if (SharedSsoToken.HasValue && SharedSsoToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return SharedSsoToken.Value.Token;
            }

            string resourceUrl = profile.EnvironmentUrl.TrimEnd('/');
            string cacheKey = $"{profile.EnvironmentUrl}:{profile.Username}:{profile.ClientId}:{profile.ConnectionString}";

            if (TokenCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return cached.Token;
            }

            await AuthSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (TokenCache.TryGetValue(cacheKey, out cached) && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    return cached.Token;
                }

                string token;

                if (!string.IsNullOrWhiteSpace(profile.ConnectionString))
                {
                    using (var serviceClient = new ServiceClient(profile.ConnectionString))
                    {
                        if (!serviceClient.IsReady)
                        {
                            throw new InvalidOperationException($"Failed to connect using connection string: {serviceClient.LastError}", serviceClient.LastException);
                        }

                        token = serviceClient.CurrentAccessToken;
                        if (string.IsNullOrEmpty(token))
                        {
                            throw new InvalidOperationException("Failed to retrieve access token from the authenticated ServiceClient.");
                        }
                    }
                }
                else
                {
                    string tenant = !string.IsNullOrWhiteSpace(profile.TenantId) ? profile.TenantId : "organizations";
                    string authority = $"https://login.microsoftonline.com/{tenant}";
                    string clientId = !string.IsNullOrWhiteSpace(profile.ClientId) ? profile.ClientId : PowerPlatformFirstPartyClientId;

                    var pca = PublicClientApplicationBuilder.Create(clientId)
                        .WithAuthority(authority)
                        .WithRedirectUri("http://localhost")
                        .Build();

                    // Primary scope: .default
                    string primaryScope = $"{resourceUrl}/.default";
                    try
                    {
                        var acquireTokenBuilder = pca.AcquireTokenInteractive(new[] { primaryScope })
                            .WithPrompt(Prompt.SelectAccount);

                        if (!string.IsNullOrWhiteSpace(profile.Username))
                        {
                            acquireTokenBuilder = acquireTokenBuilder.WithLoginHint(profile.Username);
                        }

                        var authResult = await acquireTokenBuilder.ExecuteAsync().ConfigureAwait(false);
                        token = authResult.AccessToken;
                    }
                    catch (MsalException)
                    {
                        // Fallback scope: user_impersonation
                        string fallbackScope = $"{resourceUrl}/user_impersonation";
                        var acquireTokenBuilder = pca.AcquireTokenInteractive(new[] { fallbackScope })
                            .WithPrompt(Prompt.SelectAccount);

                        if (!string.IsNullOrWhiteSpace(profile.Username))
                        {
                            acquireTokenBuilder = acquireTokenBuilder.WithLoginHint(profile.Username);
                        }

                        var authResult = await acquireTokenBuilder.ExecuteAsync().ConfigureAwait(false);
                        token = authResult.AccessToken;
                    }
                }

                var expires = DateTimeOffset.UtcNow.AddHours(1);
                TokenCache[cacheKey] = (token, expires);

                SharedSsoToken = (token, expires);

                return token;
            }
            finally
            {
                AuthSemaphore.Release();
            }
        }

        public void ClearTokenCache(string environmentUrl)
        {
            if (string.IsNullOrWhiteSpace(environmentUrl))
            {
                TokenCache.Clear();
                SharedSsoToken = null;
                return;
            }

            var keysToRemove = TokenCache.Keys.Where(k => k.StartsWith(environmentUrl, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in keysToRemove)
            {
                TokenCache.TryRemove(key, out _);
            }
        }
    }
}
