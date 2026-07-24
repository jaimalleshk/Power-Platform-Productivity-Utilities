using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.PowerPlatform.Dataverse.Client;
using PowerPlatform.ProductivityEngine.Core.Authentication;

namespace PowerPlatform.ProductivityEngine.Core.Connections
{
    public class DataverseConnectionFactory : IConnectionFactory
    {
        private readonly IAuthenticationProvider _authProvider;

        public DataverseConnectionFactory(IAuthenticationProvider authProvider = null)
        {
            _authProvider = authProvider ?? new MsalAuthenticationProvider();
        }

        public ServiceClient CreateServiceClient(ConnectionProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (!string.IsNullOrWhiteSpace(profile.ConnectionString))
            {
                var client = new ServiceClient(profile.ConnectionString);
                if (!client.IsReady)
                {
                    throw new InvalidOperationException($"Failed to connect via connection string. Error: {client.LastError}", client.LastException);
                }
                return client;
            }

            // Using token provider constructor for ServiceClient
            var uri = new Uri(profile.EnvironmentUrl);
            string token = _authProvider.GetAccessTokenAsync(profile).GetAwaiter().GetResult();
            var serviceClient = new ServiceClient(
                uri,
                (_) => Task.FromResult(token),
                useUniqueInstance: true
            );

            if (!serviceClient.IsReady)
            {
                _authProvider.ClearTokenCache(profile.EnvironmentUrl);
                throw new InvalidOperationException($"Failed to initialize ServiceClient: {serviceClient.LastError}", serviceClient.LastException);
            }

            return serviceClient;
        }

        public HttpClient CreateHttpClient(ConnectionProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var innerHandler = new HttpClientHandler();
            var throttlingHandler = new Resilience.ThrottlingHttpHandler(_authProvider, profile)
            {
                InnerHandler = innerHandler
            };

            var client = new HttpClient(throttlingHandler)
            {
                BaseAddress = new Uri(profile.EnvironmentUrl.TrimEnd('/') + "/api/data/v9.2/"),
                Timeout = TimeSpan.FromSeconds(profile.TimeoutSeconds)
            };

            // Inject Web API default headers
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("Prefer", "odata.include-annotations=\"*\"");
            client.DefaultRequestHeaders.Add("Prefer", "odata.maxpagesize=500");

            return client;
        }
    }
}
