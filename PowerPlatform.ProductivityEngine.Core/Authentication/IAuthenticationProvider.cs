using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Connections;

namespace PowerPlatform.ProductivityEngine.Core.Authentication
{
    public interface IAuthenticationProvider
    {
        Task<string> GetAccessTokenAsync(ConnectionProfile profile);
        void ClearTokenCache(string environmentUrl);
    }
}
