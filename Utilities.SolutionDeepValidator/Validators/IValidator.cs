using System.Collections.Generic;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.SolutionDeepValidator.Models;

namespace Utilities.SolutionDeepValidator.Validators
{
    public interface IValidator
    {
        string Name { get; }
        Task<List<ValidationIssue>> ValidateAsync(SolutionManifestData manifest, TargetMetadataCache cache);
    }
}
