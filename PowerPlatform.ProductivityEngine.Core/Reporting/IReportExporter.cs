using System.Threading.Tasks;

namespace PowerPlatform.ProductivityEngine.Core.Reporting
{
    public interface IReportExporter
    {
        Task ExportValidationReportAsync(ValidationReport report, string outputJsonPath, string outputHtmlPath);
        Task ExportDistillerReportAsync(DistillerReport report, string outputJsonPath);
    }
}
