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
using Microsoft.PowerPlatform.Dataverse.Client;
using PowerPlatform.ProductivityEngine.Core.Connections;
using Azure.Identity;
using Azure.Core;

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

        public async Task<List<DiscoveredTenantEnv>> DiscoverEnvironmentsAsync()
        {
            var discoveredEnvs = new List<DiscoveredTenantEnv>();

            var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = string.IsNullOrWhiteSpace(TenantId) ? "common" : TenantId,
                LoginHint = PreferredUsername
            });

            var tokenContext = new TokenRequestContext(new[] { "https://globaldisco.crm.dynamics.com/.default" });
            var tokenResult = await credential.GetTokenAsync(tokenContext).ConfigureAwait(false);
            string token = tokenResult.Token;

            SetSharedSsoToken(token, tokenResult.ExpiresOn);

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
            string scope = $"{resourceUrl}/.default";
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
                    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                    {
                        ExcludeInteractiveBrowserCredential = false
                    });

                    var tokenContext = new TokenRequestContext(new[] { scope });
                    var accessToken = await credential.GetTokenAsync(tokenContext).ConfigureAwait(false);
                    token = accessToken.Token;
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
