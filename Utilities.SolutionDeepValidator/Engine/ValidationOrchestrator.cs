using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Connections;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.SolutionDeepValidator.Models;
using Utilities.SolutionDeepValidator.Parsing;
using Utilities.SolutionDeepValidator.Validators;

namespace Utilities.SolutionDeepValidator.Engine
{
    public class ValidationOrchestrator
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly IReportExporter _reportExporter;
        private readonly bool _useSimulationMode;

        public ValidationOrchestrator(
            IConnectionFactory? connectionFactory = null, 
            IReportExporter? reportExporter = null, 
            bool useSimulationMode = false)
        {
            _connectionFactory = connectionFactory ?? new DataverseConnectionFactory();
            _reportExporter = reportExporter ?? new HtmlReportGenerator();
            _useSimulationMode = useSimulationMode;
        }

        private static readonly JsonSerializerOptions PascalCaseJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null
        };

        public async Task<ValidationResult> ExecuteValidationAsync(
            string solutionZipPath, 
            ConnectionProfile targetProfile, 
            string outputJsonPath, 
            string outputHtmlPath,
            ConnectionProfile? sourceProfile = null,
            string? solutionName = null,
            string? validationLogPath = null,
            IProgress<ProgressUpdate>? progress = null)
        {
            byte[]? byteContent = null;

            // Phase 1: Solution ZIP Acquisition (Local or Source Export)
            if (sourceProfile != null && !string.IsNullOrEmpty(solutionName))
            {
                progress?.Report(new ProgressUpdate { Stage = "Source Export", Message = $"Connecting to source environment '{sourceProfile.EnvironmentUrl}'...", PercentComplete = 5 });
                using var sourceClient = _connectionFactory.CreateHttpClient(sourceProfile);
                
                try
                {
                    byteContent = await ExportSolutionFromSourceAsync(sourceClient, solutionName, progress).ConfigureAwait(false);
                    
                    // If local path is provided, save the downloaded solution
                    if (!string.IsNullOrEmpty(solutionZipPath))
                    {
                        string? dir = Path.GetDirectoryName(solutionZipPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        await File.WriteAllBytesAsync(solutionZipPath, byteContent).ConfigureAwait(false);
                        progress?.Report(new ProgressUpdate { Stage = "Source Export", Message = $"Exported solution saved to: {solutionZipPath}", PercentComplete = 15 });
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report(new ProgressUpdate { Stage = "Source Export", Message = $"Failed to export solution from source: {ex.Message}", Status = ProgressStatus.Error });
                    throw;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(solutionZipPath) || !File.Exists(solutionZipPath))
                    throw new FileNotFoundException("Solution zip package not found.", solutionZipPath);

                progress?.Report(new ProgressUpdate { Stage = "Reading Package", Message = $"Reading local solution package: {solutionZipPath}...", PercentComplete = 10 });
                byteContent = await File.ReadAllBytesAsync(solutionZipPath).ConfigureAwait(false);
            }

            // Phase 2: Local Manifest Parsing
            progress?.Report(new ProgressUpdate { Stage = "Parsing Manifests", Message = "Extracting solution.xml and customizations.xml in-memory...", PercentComplete = 20 });
            var packager = new SolutionPackagerWrapper();
            var manifestData = packager.ParseSolutionZip(byteContent);
            progress?.Report(new ProgressUpdate { Stage = "Parsing Manifests", Message = $"Parsed solution '{manifestData.UniqueName}', version '{manifestData.Version}'.", PercentComplete = 25 });

            // Create client for target
            using var targetClient = _connectionFactory.CreateHttpClient(targetProfile);
            var allIssues = new List<ValidationIssue>();

            // Phase 3: Platform Staging Action
            progress?.Report(new ProgressUpdate { Stage = "Platform Staging", Message = "Executing StageSolution action on target environment...", PercentComplete = 30 });
            try
            {
                var stagingIssues = await ExecuteStageSolutionAsync(targetClient, byteContent, progress).ConfigureAwait(false);
                allIssues.AddRange(stagingIssues);
            }
            catch (Exception ex)
            {
                progress?.Report(new ProgressUpdate { Stage = "Platform Staging", Message = $"Staging action failed: {ex.Message}", Status = ProgressStatus.Warning });
                allIssues.Add(new ValidationIssue
                {
                    Id = "ERR-STAGE",
                    Severity = "Red",
                    ComponentType = "Solution",
                    LogicalName = manifestData.UniqueName,
                    Description = $"StageSolution validation failed: {ex.Message}"
                });
            }

            // Phase 4: Target Metadata Crawling
            progress?.Report(new ProgressUpdate { Stage = "Target Crawling", Message = "Crawling target metadata schema and components...", PercentComplete = 40 });
            var crawler = new TargetEnvironmentCrawler(targetClient, _useSimulationMode);
            var targetCache = await crawler.CrawlTargetMetadataAsync(manifestData.Entities, progress).ConfigureAwait(false);

            // Phase 5: Executing the 19 Modular Validators
            progress?.Report(new ProgressUpdate { Stage = "Validation Engine", Message = "Initializing 19 validation checkers...", PercentComplete = 70 });
            var validators = new List<IValidator>
            {
                new SolutionVersionValidator(),
                new MissingDependencyValidator(),
                new EntityValidator(),
                new AttributeValidator(),
                new RelationshipValidator(),
                new OptionSetValidator(),
                new SchemaConflictValidator(),
                new ManagedPropertyValidator(),
                new ComponentOwnershipValidator(),
                new WorkflowValidator(),
                new PluginValidator(),
                new WebResourceValidator(),
                new SecurityRoleValidator(),
                new ConnectionRefValidator(),
                new AppVersionValidator(),
                new EnvironmentVariableValidator(),
                new AppActionValidator(),
                new FormulaValidator(),
                new RibbonValidator()
            };

            double valProgressStep = 20.0 / validators.Count;
            double valPercent = 70.0;

            foreach (var validator in validators)
            {
                valPercent += valProgressStep;
                progress?.Report(new ProgressUpdate { Stage = "Validation Engine", Message = $"Running validator: {validator.Name}...", PercentComplete = valPercent });
                try
                {
                    var results = await validator.ValidateAsync(manifestData, targetCache).ConfigureAwait(false);
                    allIssues.AddRange(results);
                }
                catch (Exception ex)
                {
                    allIssues.Add(new ValidationIssue
                    {
                        Id = "VALIDATOR_ERROR",
                        Severity = "Red",
                        ComponentType = "Validator",
                        LogicalName = validator.Name,
                        Description = $"Validator '{validator.Name}' threw an exception: {ex.Message}"
                    });
                }
            }

            // Phase 6: Parse External Validation Log (if provided)
            if (!string.IsNullOrEmpty(validationLogPath))
            {
                progress?.Report(new ProgressUpdate { Stage = "Log Parsing", Message = $"Parsing D365 validation errors log: {validationLogPath}...", PercentComplete = 92 });
                var logParser = new XmlValidationLogParser();
                var logIssues = logParser.ParseLogFile(validationLogPath);
                allIssues.AddRange(logIssues);
                progress?.Report(new ProgressUpdate { Stage = "Log Parsing", Message = $"Parsed {logIssues.Count} errors from log file.", PercentComplete = 95 });
            }

            // Phase 7: Score Confidence and Compute Results
            progress?.Report(new ProgressUpdate { Stage = "Reporting", Message = "Compiling final reports...", PercentComplete = 98 });
            
            int blockerCount = 0;
            int warningCount = 0;
            foreach (var issue in allIssues)
            {
                if (issue.Severity.Equals("Red", StringComparison.OrdinalIgnoreCase)) blockerCount++;
                else if (issue.Severity.Equals("Yellow", StringComparison.OrdinalIgnoreCase)) warningCount++;
            }

            // Calculate Confidence Score (High, Medium, Low)
            string confidenceScore = "High";
            if (blockerCount > 0 || targetCache.MetadataGaps.Count >= 3)
            {
                confidenceScore = "Low";
            }
            else if (warningCount > 0 || targetCache.MetadataGaps.Count > 0)
            {
                confidenceScore = "Medium";
            }

            var report = new ValidationReport
            {
                SolutionName = manifestData.UniqueName,
                SourceVersion = manifestData.Version,
                TargetEnvironment = targetProfile.EnvironmentUrl,
                ValidationTimestamp = DateTime.UtcNow,
                OverallResult = blockerCount > 0 ? "Failed" : (warningCount > 0 ? "PassedWithWarnings" : "Passed"),
                ConfidenceScore = confidenceScore,
                Metrics = new ValidationMetrics
                {
                    TotalComponentsEvaluated = manifestData.Components.Count + manifestData.Entities.Count,
                    BlockersCount = blockerCount,
                    WarningsCount = warningCount
                },
                Issues = allIssues,
                MetadataGaps = new List<string>(targetCache.MetadataGaps)
            };

            // Phase 8: Export Reports
            await _reportExporter.ExportValidationReportAsync(report, outputJsonPath, outputHtmlPath).ConfigureAwait(false);
            
            progress?.Report(new ProgressUpdate { Stage = "Reporting", Message = "JSON and HTML reports generated successfully.", PercentComplete = 100.0 });

            return new ValidationResult(blockerCount == 0, report);
        }

        public async Task<byte[]> ExportSolutionFromSourceAsync(
            HttpClient sourceClient, 
            string solutionName, 
            IProgress<ProgressUpdate>? progress = null)
        {
            if (_useSimulationMode)
            {
                progress?.Report(new ProgressUpdate { Stage = "Source Export", Message = "(Simulation) Exporting solution package from source...", PercentComplete = 10 });
                await Task.Delay(800).ConfigureAwait(false);
                // Create a temporary mock solution ZIP content
                string mockZip = Path.Combine(Path.GetTempPath(), $"MockExport_{Guid.NewGuid():N}.zip");
                // Create minimal solution file content (we can use dummy byte content representing an empty zip)
                byte[] mockBytes = new byte[] { 80, 75, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // empty zip header
                return mockBytes;
            }

            var payload = new
            {
                SolutionName = solutionName,
                Managed = false,
                ExportAutoNumberingSettings = false,
                ExportCalendarSettings = false,
                ExportCustomizationSettings = false,
                ExportEmailTrackingSettings = false,
                ExportGeneralSettings = false,
                ExportIsvConfigSettings = false,
                ExportMarketingSettings = false,
                ExportOutlookSettings = false,
                ExportRelationshipRoles = false,
                ExportSalesReceivingSettings = false,
                ExportExternalApplications = false
            };

            var response = await sourceClient.PostAsJsonAsync("ExportSolution", payload, PascalCaseJsonOptions).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string errContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"ExportSolution failed: {response.StatusCode}. Details: {errContent}");
            }

            using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
            if (doc == null || !doc.RootElement.TryGetProperty("ExportSolutionFile", out var fileProp))
            {
                throw new InvalidOperationException("ExportSolution response did not contain 'ExportSolutionFile'.");
            }

            string base64File = fileProp.GetString() ?? throw new InvalidOperationException("ExportSolutionFile is null.");
            return Convert.FromBase64String(base64File);
        }

        private async Task<List<ValidationIssue>> ExecuteStageSolutionAsync(
            HttpClient client, 
            byte[] zipBytes, 
            IProgress<ProgressUpdate>? progress)
        {
            var issues = new List<ValidationIssue>();

            if (_useSimulationMode)
            {
                progress?.Report(new ProgressUpdate { Stage = "Platform Staging", Message = "(Simulation) Dispatching StageSolution async job...", PercentComplete = 35 });
                await Task.Delay(600).ConfigureAwait(false);
                // Return one warning for compatibility checks
                issues.Add(new ValidationIssue
                {
                    Id = "ERR-STG-002",
                    Severity = "Yellow",
                    ComponentType = "Solution",
                    LogicalName = "EnterpriseCoreModifications",
                    Description = "Target organization version is newer than solution package. Check compatibility.",
                    ResolutionUrl = "https://learn.microsoft.com/en-us/power-platform/alm/solution-layers"
                });
                return issues;
            }

            // Convert to Base64 for the StageSolution action payload
            string base64Zip = Convert.ToBase64String(zipBytes);
            var payload = new Dictionary<string, object>
            {
                ["CustomizationFile"] = base64Zip
            };

            // 1. Post to StageSolution
            var postResponse = await client.PostAsJsonAsync("StageSolution", payload, PascalCaseJsonOptions).ConfigureAwait(false);
            if (!postResponse.IsSuccessStatusCode)
            {
                string errorContent = await postResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"StageSolution POST request failed. StatusCode: {postResponse.StatusCode}, Error: {errorContent}");
            }

            // Read the StageSolutionStatusId
            using var responseDoc = await postResponse.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
            if (responseDoc == null || !responseDoc.RootElement.TryGetProperty("StageSolutionStatusId", out var idProp))
            {
                throw new InvalidOperationException("Response from StageSolution action did not contain StageSolutionStatusId.");
            }

            string statusId = idProp.GetString() ?? throw new InvalidOperationException("StageSolutionStatusId is null.");
            progress?.Report(new ProgressUpdate { Stage = "Platform Staging", Message = $"Staging job initiated. Status ID: {statusId}. Polling...", PercentComplete = 33 });

            // 2. Poll for status
            int pollCount = 0;
            int maxPolls = 30; // 30 attempts, 5 seconds apart = 150 seconds max
            while (pollCount < maxPolls)
            {
                await Task.Delay(5000).ConfigureAwait(false);
                pollCount++;

                var getResponse = await client.GetAsync($"msdyn_stagesolutionstatuses({statusId})").ConfigureAwait(false);
                if (!getResponse.IsSuccessStatusCode)
                {
                    progress?.Report(new ProgressUpdate { Stage = "Platform Staging", Message = $"Polling failed on attempt {pollCount} (status {getResponse.StatusCode}). Retrying...", Status = ProgressStatus.Warning });
                    continue;
                }

                using var statusDoc = await getResponse.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
                if (statusDoc == null) continue;

                var root = statusDoc.RootElement;
                if (root.TryGetProperty("msdyn_status", out var statusProp))
                {
                    int statusVal = statusProp.GetInt32();
                    if (statusVal == 1) // Staged / In Progress
                    {
                        progress?.Report(new ProgressUpdate { Stage = "Platform Staging", Message = $"Staging in progress (Attempt {pollCount}/{maxPolls})...", PercentComplete = 33 + (pollCount * 0.2) });
                        continue;
                    }

                    if (statusVal == 2) // Succeeded
                    {
                        progress?.Report(new ProgressUpdate { Stage = "Platform Staging", Message = "Staging completed successfully.", PercentComplete = 38 });
                        break;
                    }

                    if (statusVal == 3) // Failed
                    {
                        progress?.Report(new ProgressUpdate { Stage = "Platform Staging", Message = "Staging failed. Retrieving error details...", Status = ProgressStatus.Error, PercentComplete = 38 });
                        if (root.TryGetProperty("msdyn_validationdata", out var valDataProp))
                        {
                            string xmlValidationData = valDataProp.GetString() ?? string.Empty;
                            ParseStagingValidationData(xmlValidationData, issues);
                        }
                        break;
                    }
                }
            }

            return issues;
        }

        private void ParseStagingValidationData(string xmlValidationData, List<ValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(xmlValidationData)) return;

            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(xmlValidationData);
                var errorNodes = doc.Descendants("ImportError");
                foreach (var err in errorNodes)
                {
                    string code = err.Element("ErrorCode")?.Value ?? "ERR-STG";
                    string desc = err.Element("Description")?.Value ?? "Unknown staging error";
                    string compName = err.Element("SchemaName")?.Value ?? string.Empty;
                    string compType = err.Element("Type")?.Value ?? "Component";

                    issues.Add(new ValidationIssue
                    {
                        Id = code,
                        Severity = "Red",
                        ComponentType = compType,
                        LogicalName = compName,
                        Description = $"Staging Error: {desc}"
                    });
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    Id = "ERR-STG-PARSE",
                    Severity = "Red",
                    ComponentType = "StagingData",
                    Description = $"Failed to parse staging validation logs: {ex.Message}"
                });
            }
        }
    }
}
