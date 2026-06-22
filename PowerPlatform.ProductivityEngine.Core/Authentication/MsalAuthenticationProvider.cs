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
        // Cache for access tokens retrieved via connection strings to avoid recreating ServiceClient repeatedly
        private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)> TokenCache = 
            new ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)>();

        private static readonly SemaphoreSlim AuthSemaphore = new SemaphoreSlim(1, 1);

        public async Task<string> GetAccessTokenAsync(ConnectionProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

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
                // 2. Interactive Auth Flow using Azure.Identity.InteractiveBrowserCredential (as requested by user)
                else if (profile.UseInteractiveAuth && 
                         string.IsNullOrWhiteSpace(profile.ClientSecret) && 
                         string.IsNullOrWhiteSpace(profile.ClientCertificateThumbprint) && 
                         string.IsNullOrWhiteSpace(profile.Username))
                {
                    var options = new InteractiveBrowserCredentialOptions
                    {
                        ClientId = !string.IsNullOrWhiteSpace(profile.ClientId) ? profile.ClientId : "51f81489-12ee-4a9e-aaae-a2591f45987d",
                        RedirectUri = !string.IsNullOrWhiteSpace(profile.RedirectUri) ? new Uri(profile.RedirectUri) : new Uri("http://localhost")
                    };

                    if (!string.IsNullOrWhiteSpace(profile.TenantId) && profile.TenantId != "organizations" && profile.TenantId != "common")
                    {
                        options.TenantId = profile.TenantId;
                    }

                    if (!string.IsNullOrWhiteSpace(profile.LoginHint))
                    {
                        options.LoginHint = profile.LoginHint;
                    }

                    var credential = new InteractiveBrowserCredential(options);
                    var tokenRequestContext = new TokenRequestContext(new[] { scope });
                    var tokenResult = await credential.GetTokenAsync(tokenRequestContext).ConfigureAwait(false);
                    token = tokenResult.Token;
                }
                // 3. Fallback: Build standard connection string and let ServiceClient resolve it
                else
                {
                    string connStr = $"Url={resourceUrl};";

                    if (!string.IsNullOrWhiteSpace(profile.ClientId))
                    {
                        connStr += $"ClientId={profile.ClientId};";
                    }
                    if (!string.IsNullOrWhiteSpace(profile.RedirectUri))
                    {
                        connStr += $"RedirectUri={profile.RedirectUri};";
                    }
                    if (!string.IsNullOrWhiteSpace(profile.LoginHint))
                    {
                        connStr += $"LoginHint={profile.LoginHint};";
                    }

                    if (!string.IsNullOrWhiteSpace(profile.ClientSecret))
                    {
                        connStr += $"AuthType=ClientSecret;ClientSecret={profile.ClientSecret};";
                    }
                    else if (!string.IsNullOrWhiteSpace(profile.ClientCertificateThumbprint))
                    {
                        connStr += $"AuthType=Certificate;Thumbprint={profile.ClientCertificateThumbprint};";
                    }
                    else if (!string.IsNullOrWhiteSpace(profile.Username) && !string.IsNullOrWhiteSpace(profile.Password))
                    {
                        connStr += $"AuthType=Office365;Username={profile.Username};Password={profile.Password};";
                    }
                    else
                    {
                        throw new InvalidOperationException("No valid authentication credentials were provided in the connection profile.");
                    }

                    using (var serviceClient = new ServiceClient(connStr))
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

                TokenCache[cacheKey] = (token, DateTimeOffset.UtcNow.AddHours(1));
                return token;
            }
            finally
            {
                AuthSemaphore.Release();
            }
        }

        public void ClearTokenCache(string environmentUrl)
        {
            var keysToRemove = TokenCache.Keys.Where(k => k.Contains(environmentUrl)).ToList();
            foreach (var key in keysToRemove)
            {
                TokenCache.TryRemove(key, out _);
            }
        }
    }
}
