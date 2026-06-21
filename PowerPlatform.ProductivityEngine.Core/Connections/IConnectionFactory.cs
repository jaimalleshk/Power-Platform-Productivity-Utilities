using System.Net.Http;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace PowerPlatform.ProductivityEngine.Core.Connections
{
    public interface IConnectionFactory
    {
        ServiceClient CreateServiceClient(ConnectionProfile profile);
        HttpClient CreateHttpClient(ConnectionProfile profile);
    }
}
