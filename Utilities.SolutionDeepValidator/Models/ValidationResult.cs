using PowerPlatform.ProductivityEngine.Core.Reporting;

namespace Utilities.SolutionDeepValidator.Models
{
    public class ValidationResult
    {
        public bool IsSuccess { get; set; }
        public ValidationReport Report { get; set; }

        public ValidationResult(bool isSuccess, ValidationReport report)
        {
            IsSuccess = isSuccess;
            Report = report;
        }
    }
}
