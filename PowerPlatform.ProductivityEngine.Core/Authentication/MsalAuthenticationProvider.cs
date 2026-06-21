using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.PowerPlatform.Dataverse.Client;
using PowerPlatform.ProductivityEngine.Core.Connections;

namespace PowerPlatform.ProductivityEngine.Core.Authentication
{
    public class MsalAuthenticationProvider : IAuthenticationProvider
    {
        private static readonly ConcurrentDictionary<string, IConfidentialClientApplication> ConfidentialApps = 
            new ConcurrentDictionary<string, IConfidentialClientApplication>();

        private static readonly ConcurrentDictionary<string, IPublicClientApplication> PublicApps = 
            new ConcurrentDictionary<string, IPublicClientApplication>();

        // Cache for access tokens retrieved via connection strings to avoid recreating ServiceClient repeatedly
        private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)> TokenCacheByConnStr = 
            new ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)>();

        public async Task<string> GetAccessTokenAsync(ConnectionProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            // Format scope
            string resourceUrl = profile.EnvironmentUrl.TrimEnd('/');
            string scope = $"{resourceUrl}/.default";
            string[] scopes = new[] { scope };

            // 1. If connection string is provided, use ServiceClient to acquire token
            if (!string.IsNullOrWhiteSpace(profile.ConnectionString))
            {
                string cacheKey = profile.ConnectionString;
                if (TokenCacheByConnStr.TryGetValue(cacheKey, out var cached) && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    return cached.Token;
                }

                // Initialize ServiceClient to get token
                using (var serviceClient = new ServiceClient(profile.ConnectionString))
                {
                    if (!serviceClient.IsReady)
                    {
                        throw new InvalidOperationException($"Failed to connect using connection string: {serviceClient.LastError}", serviceClient.LastException);
                    }

                    string token = serviceClient.CurrentAccessToken;
                    if (string.IsNullOrEmpty(token))
                    {
                        throw new InvalidOperationException("Failed to retrieve access token from the authenticated ServiceClient.");
                    }

                    // Store token cache
                    TokenCacheByConnStr[cacheKey] = (token, DateTimeOffset.UtcNow.AddHours(1));
                    return token;
                }
            }

            // 2. Interactive Auth Flow
            if (profile.UseInteractiveAuth)
            {
                string appKey = $"Interactive:{profile.TenantId}:{profile.ClientId}";
                var app = PublicApps.GetOrAdd(appKey, _ =>
                {
                    var builder = PublicClientApplicationBuilder.Create(profile.ClientId)
                        .WithRedirectUri("http://localhost");

                    if (!string.IsNullOrWhiteSpace(profile.TenantId))
                    {
                        builder.WithTenantId(profile.TenantId);
                    }

                    return builder.Build();
                });

                var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
                try
                {
                    var silentResult = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                        .ExecuteAsync()
                        .ConfigureAwait(false);
                    return silentResult.AccessToken;
                }
                catch (MsalUiRequiredException)
                {
                    var interactiveResult = await app.AcquireTokenInteractive(scopes)
                        .ExecuteAsync()
                        .ConfigureAwait(false);
                    return interactiveResult.AccessToken;
                }
            }

            // 3. Username/Password Auth Flow
            if (!string.IsNullOrWhiteSpace(profile.Username) && !string.IsNullOrWhiteSpace(profile.Password))
            {
                string appKey = $"UserPass:{profile.TenantId}:{profile.ClientId}";
                var app = PublicApps.GetOrAdd(appKey, _ =>
                {
                    var builder = PublicClientApplicationBuilder.Create(profile.ClientId);
                    if (!string.IsNullOrWhiteSpace(profile.TenantId))
                    {
                        builder.WithTenantId(profile.TenantId);
                    }
                    return builder.Build();
                });

                var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
                try
                {
                    var silentResult = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                        .ExecuteAsync()
                        .ConfigureAwait(false);
                    return silentResult.AccessToken;
                }
                catch (Exception)
                {
                    using (var securePassword = new SecureString())
                    {
                        foreach (char c in profile.Password)
                        {
                            securePassword.AppendChar(c);
                        }
                        securePassword.MakeReadOnly();

                        var userPassResult = await app.AcquireTokenByUsernamePassword(scopes, profile.Username, securePassword)
                            .ExecuteAsync()
                            .ConfigureAwait(false);
                        return userPassResult.AccessToken;
                    }
                }
            }

            // 4. Confidential Client Flow (Client Secret or Certificate)
            if (!string.IsNullOrWhiteSpace(profile.ClientId) && (!string.IsNullOrWhiteSpace(profile.ClientSecret) || !string.IsNullOrWhiteSpace(profile.ClientCertificateThumbprint)))
            {
                string appKey = $"Confidential:{profile.TenantId}:{profile.ClientId}";
                var app = ConfidentialApps.GetOrAdd(appKey, _ =>
                {
                    var builder = ConfidentialClientApplicationBuilder.Create(profile.ClientId);

                    if (!string.IsNullOrWhiteSpace(profile.ClientSecret))
                    {
                        builder.WithClientSecret(profile.ClientSecret);
                    }
                    // Certificate auth is supported if needed, but client secret is default confidential app client here.
                    
                    if (!string.IsNullOrWhiteSpace(profile.TenantId))
                    {
                        builder.WithTenantId(profile.TenantId);
                    }

                    return builder.Build();
                });

                var clientResult = await app.AcquireTokenForClient(scopes)
                    .ExecuteAsync()
                    .ConfigureAwait(false);
                return clientResult.AccessToken;
            }

            throw new InvalidOperationException("No valid authentication credentials were provided in the connection profile.");
        }

        public void ClearTokenCache(string environmentUrl)
        {
            // Clear in-memory token cache for connection strings
            var keysToRemove = TokenCacheByConnStr.Keys.Where(k => k.Contains(environmentUrl)).ToList();
            foreach (var key in keysToRemove)
            {
                TokenCacheByConnStr.TryRemove(key, out _);
            }

            // Note: MSAL apps manage their own caches, but clearing cached accounts can be done if needed.
            foreach (var app in PublicApps.Values)
            {
                var accounts = app.GetAccountsAsync().GetAwaiter().GetResult();
                foreach (var account in accounts)
                {
                    app.RemoveAsync(account).GetAwaiter().GetResult();
                }
            }
        }
    }
}
