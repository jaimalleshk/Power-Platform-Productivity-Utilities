using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerPlatform.Dataverse.Client;
using PowerPlatform.ProductivityEngine.Core.Connections;

namespace PowerPlatform.ProductivityEngine.Core.Authentication
{
    public class MsalAuthenticationProvider : IAuthenticationProvider
    {
        // Cache for access tokens retrieved via connection strings to avoid recreating ServiceClient repeatedly
        private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)> TokenCacheByConnStr = 
            new ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresOn)>();

        public async Task<string> GetAccessTokenAsync(ConnectionProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            // If connection string is not provided, build it dynamically from profile details
            if (string.IsNullOrWhiteSpace(profile.ConnectionString))
            {
                string connStr = $"Url={profile.EnvironmentUrl.TrimEnd('/')};";

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
                else if (profile.UseInteractiveAuth)
                {
                    connStr += "AuthType=OAuth;LoginPrompt=Auto;";
                }
                else
                {
                    throw new InvalidOperationException("No valid authentication credentials were provided in the connection profile.");
                }

                profile.ConnectionString = connStr;
            }

            string cacheKey = profile.ConnectionString;
            if (TokenCacheByConnStr.TryGetValue(cacheKey, out var cached) && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return cached.Token;
            }

            // Initialize ServiceClient to retrieve the token
            using (var serviceClient = new ServiceClient(profile.ConnectionString))
            {
                if (!serviceClient.IsReady)
                {
                    throw new InvalidOperationException($"Failed to connect using ServiceClient connection string: {serviceClient.LastError}", serviceClient.LastException);
                }

                string token = serviceClient.CurrentAccessToken;
                if (string.IsNullOrEmpty(token))
                {
                    throw new InvalidOperationException("Failed to retrieve access token from the authenticated ServiceClient.");
                }

                // Store token in cache
                TokenCacheByConnStr[cacheKey] = (token, DateTimeOffset.UtcNow.AddHours(1));
                return token;
            }
        }

        public void ClearTokenCache(string environmentUrl)
        {
            // Clear in-memory token cache
            var keysToRemove = TokenCacheByConnStr.Keys.Where(k => k.Contains(environmentUrl)).ToList();
            foreach (var key in keysToRemove)
            {
                TokenCacheByConnStr.TryRemove(key, out _);
            }
        }
    }
}
