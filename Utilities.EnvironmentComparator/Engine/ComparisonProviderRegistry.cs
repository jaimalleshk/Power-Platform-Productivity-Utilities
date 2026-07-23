using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine
{
    public class ComparisonProviderRegistry
    {
        private static readonly List<IComparisonProvider> RegisteredProviders = new();

        static ComparisonProviderRegistry()
        {
            AutoRegisterBuiltInProviders();
        }

        public static void RegisterProvider(IComparisonProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (!RegisteredProviders.Any(p => p.ProviderName.Equals(provider.ProviderName, StringComparison.OrdinalIgnoreCase)))
            {
                RegisteredProviders.Add(provider);
            }
        }

        public static IReadOnlyList<IComparisonProvider> GetRegisteredProviders()
        {
            return RegisteredProviders.OrderBy(p => p.ExecutionOrder).ToList().AsReadOnly();
        }

        public static void AutoRegisterBuiltInProviders()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var providerTypes = assembly.GetTypes()
                    .Where(t => typeof(IComparisonProvider).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in providerTypes)
                {
                    if (Activator.CreateInstance(type) is IComparisonProvider provider)
                    {
                        RegisterProvider(provider);
                    }
                }
            }
            catch { }
        }

        public static async Task ExecuteAllProvidersAsync(HttpClient client, string envName, RawEnvData rawData, ComparisonScope scope)
        {
            foreach (var provider in GetRegisteredProviders())
            {
                try
                {
                    await provider.CrawlAsync(client, envName, rawData, scope).ConfigureAwait(false);
                }
                catch
                {
                    // Graceful fallback for custom providers
                }
            }
        }
    }
}
