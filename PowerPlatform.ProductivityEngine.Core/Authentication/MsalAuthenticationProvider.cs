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

        public static async Task<string> AutoDiscoverTenantIdAsync(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return "organizations";

            if (domain.Contains('@'))
            {
                domain = domain.Split('@')[1];
            }

            try
            {
                using var client = new HttpClient();
                string url = $"https://login.microsoftonline.com/{domain}/v2.0/.well-known/openid-configuration";
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

            return domain;
        }

        public async Task<List<DiscoveredTenantEnv>> DiscoverEnvironmentsAsync(Action<string>? progressCallback = null)
        {
            var discoveredEnvs = new List<DiscoveredTenantEnv>();

            progressCallback?.Invoke("Resolving Azure Tenant ID from user domain...");

            string effectiveTenant = string.IsNullOrWhiteSpace(TenantId)
                ? await AutoDiscoverTenantIdAsync(PreferredUsername ?? "").ConfigureAwait(false)
                : TenantId;

            progressCallback?.Invoke($"Acquiring OAuth Token for tenant ({effectiveTenant})...");

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
                authResult = await AcquireTokenWithFallbacksAsync(pca, scopes, PreferredUsername).ConfigureAwait(false);
            }
            catch
            {
                // Fallback scope attempt: user_impersonation
                scopes = new[] { "https://globaldisco.crm.dynamics.com/user_impersonation" };
                authResult = await AcquireTokenWithFallbacksAsync(pca, scopes, PreferredUsername).ConfigureAwait(false);
            }

            string token = authResult.AccessToken;
            SetSharedSsoToken(token, authResult.ExpiresOn);

            progressCallback?.Invoke("Querying Dataverse Global Discovery Service endpoints (v2.0 & v9.2)...");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            string[] endpoints = new[]
            {
                "https://globaldisco.crm.dynamics.com/api/discovery/v2.0/Instances",
                "https://globaldisco.crm.dynamics.com/api/discovery/v9.2/Instances",
                "https://globaldisco.crm.dynamics.com/api/discovery/v9.0/Instances"
            };

            foreach (var ep in endpoints)
            {
                try
                {
                    progressCallback?.Invoke($"Querying Global Discovery Endpoint: {ep}");
                    var res = await httpClient.GetAsync(ep).ConfigureAwait(false);
                    if (res.IsSuccessStatusCode)
                    {
                        using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                        if (doc != null && doc.RootElement.TryGetProperty("value", out var valArr) && valArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in valArr.EnumerateArray())
                            {
                                string name = item.TryGetProperty("FriendlyName", out var fn) ? fn.GetString() ?? "" : "";
                                string url = item.TryGetProperty("Url", out var u) ? u.GetString() ?? "" : "";
                                if (string.IsNullOrEmpty(url) && item.TryGetProperty("ApiUrl", out var au))
                                {
                                    url = au.GetString() ?? "";
                                }
                                string envId = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "" : "";

                                if (string.IsNullOrEmpty(name))
                                {
                                    name = item.TryGetProperty("UniqueName", out var un) ? un.GetString() ?? "" : "Dataverse Environment";
                                }

                                if (!string.IsNullOrEmpty(url) && !discoveredEnvs.Any(e => e.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                                {
                                    discoveredEnvs.Add(new DiscoveredTenantEnv
                                    {
                                        Name = name,
                                        Url = url,
                                        EnvironmentId = envId
                                    });
                                }
                            }
                        }

                        if (discoveredEnvs.Count > 0)
                        {
                            break; // Stop after first successful endpoint return
                        }
                    }
                }
                catch
                {
                    // Fallback to next endpoint
                }
            }

            return discoveredEnvs;
        }

        public async Task<string> GetAccessTokenAsync(ConnectionProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            string resourceUrl = profile.EnvironmentUrl.TrimEnd('/');
            string cacheKey = $"{resourceUrl}_{profile.Username}";

            if (TokenCache.TryGetValue(cacheKey, out var cachedToken))
            {
                if (DateTimeOffset.UtcNow.AddMinutes(5) < cachedToken.ExpiresOn)
                {
                    return cachedToken.Token;
                }
            }

            if (SharedSsoToken.HasValue && DateTimeOffset.UtcNow.AddMinutes(5) < SharedSsoToken.Value.ExpiresOn)
            {
                TokenCache[cacheKey] = SharedSsoToken.Value;
                return SharedSsoToken.Value.Token;
            }

            await AuthSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (TokenCache.TryGetValue(cacheKey, out cachedToken))
                {
                    if (DateTimeOffset.UtcNow.AddMinutes(5) < cachedToken.ExpiresOn)
                    {
                        return cachedToken.Token;
                    }
                }

                string token = string.Empty;

                if (!string.IsNullOrWhiteSpace(profile.ConnectionString))
                {
                    if (profile.ConnectionString.Contains("AuthType=OAuth", StringComparison.OrdinalIgnoreCase) ||
                        profile.ConnectionString.Contains("AuthType=ClientSecret", StringComparison.OrdinalIgnoreCase))
                    {
                        using var serviceClient = new ServiceClient(profile.ConnectionString);
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

                    string[] primaryScope = new[] { $"{resourceUrl}/.default" };
                    try
                    {
                        var authResult = await AcquireTokenWithFallbacksAsync(pca, primaryScope, profile.Username).ConfigureAwait(false);
                        token = authResult.AccessToken;
                    }
                    catch (MsalException)
                    {
                        string[] fallbackScope = new[] { $"{resourceUrl}/user_impersonation" };
                        var authResult = await AcquireTokenWithFallbacksAsync(pca, fallbackScope, profile.Username).ConfigureAwait(false);
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

        private static async Task<AuthenticationResult> AcquireTokenWithFallbacksAsync(IPublicClientApplication pca, string[] scopes, string? username)
        {
            // Attempt 1: Embedded WebView popup window
            try
            {
                var builder = pca.AcquireTokenInteractive(scopes)
                    .WithPrompt(Prompt.SelectAccount)
                    .WithUseEmbeddedWebView(true);

                if (!string.IsNullOrWhiteSpace(username))
                {
                    builder = builder.WithLoginHint(username);
                }

                return await builder.ExecuteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Attempt 2: Fallback to System Web Browser if embedded WebView fails or is blocked on OS
                var builder = pca.AcquireTokenInteractive(scopes)
                    .WithPrompt(Prompt.SelectAccount)
                    .WithUseEmbeddedWebView(false);

                if (!string.IsNullOrWhiteSpace(username))
                {
                    builder = builder.WithLoginHint(username);
                }

                return await builder.ExecuteAsync().ConfigureAwait(false);
            }
        }

        public void ClearTokenCache(string environmentUrl)
        {
            if (string.IsNullOrWhiteSpace(environmentUrl))
            {
                TokenCache.Clear();
                SharedSsoToken = null;
            }
            else
            {
                string resourceUrl = environmentUrl.TrimEnd('/');
                var keysToRemove = TokenCache.Keys.Where(k => k.StartsWith(resourceUrl, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var key in keysToRemove)
                {
                    TokenCache.TryRemove(key, out _);
                }
            }
        }
    }
}
