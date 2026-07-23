using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerPlatform.Dataverse.Client;
using PowerPlatform.ProductivityEngine.Core.Connections;
using Azure.Identity;
using Azure.Core;

namespace PowerPlatform.ProductivityEngine.Core.Authentication
{
    public class MsalAuthenticationProvider : IAuthenticationProvider
    {
        private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)> TokenCache = 
            new ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)>();

        private static (string Token, DateTimeOffset ExpiresOn)? SharedSsoToken;

        private static readonly SemaphoreSlim AuthSemaphore = new SemaphoreSlim(1, 1);

        public void SetSharedSsoToken(string token, DateTimeOffset expiresOn)
        {
            SharedSsoToken = (token, expiresOn);
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

                // 1. If connection string is provided, use ServiceClient to acquire token
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
                // 2. Interactive / DefaultAzureCredential single login
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

                // Set as shared SSO token for single-login across all environments and Power Platform APIs
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
