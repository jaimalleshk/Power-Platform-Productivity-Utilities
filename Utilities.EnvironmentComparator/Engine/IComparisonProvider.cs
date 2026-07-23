using System;
using System.Net.Http;
using System.Threading.Tasks;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine
{
    /// <summary>
    /// Extensible Interface for adding custom component comparison providers to the Environment Comparison Tool.
    /// Allows developers to extend support for any new Dataverse entity, Power Platform asset, or custom API.
    /// </summary>
    public interface IComparisonProvider
    {
        /// <summary>
        /// Unique Name of the Provider (e.g. "PowerPagesProvider", "AIModelProvider")
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Category folder in Solution Explorer (e.g. "PowerPages", "AIModels", "Dataflows")
        /// </summary>
        string TargetCategory { get; }

        /// <summary>
        /// Order priority during metadata crawling (lower numbers execute first)
        /// </summary>
        int ExecutionOrder => 100;

        /// <summary>
        /// Asynchronously crawls metadata from D365 Web API / Power Platform APIs and populates rawData.
        /// </summary>
        Task CrawlAsync(HttpClient client, string envName, RawEnvData rawData, ComparisonScope scope);
    }
}
