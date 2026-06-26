using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Connections;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.SolutionDeepValidator.Engine;
using Utilities.SolutionDeepValidator.Models;
using Utilities.SolutionRepairDistiller.Engine;
using Utilities.SolutionRepairDistiller.Models;
using Utilities.UserMultiEnvManager.Engine;
using Utilities.UserMultiEnvManager.Models;

namespace PowerPlatform.ProductivityEngine.ConsoleUX
{
    class Program
    {
        private static void SafeSetConsoleTitle(string title)
        {
            try { Console.Title = title; } catch { }
        }

        private static void SafeSetForegroundColor(ConsoleColor color)
        {
            try { Console.ForegroundColor = color; } catch { }
        }

        private static void SafeResetConsoleColor()
        {
            try { Console.ResetColor(); } catch { }
        }

        static async Task<int> Main(string[] args)
        {
            try
            {
                SafeSetConsoleTitle("Power Platform Productivity Suite");
                SafeSetForegroundColor(ConsoleColor.Cyan);
                Console.WriteLine(@"
╔════════════════════════════════════════════════════════════════╗
║             POWER PLATFORM PRODUCTIVITY ENGINE CLI             ║
║               Libraries & Console Orchestrator                 ║
╚════════════════════════════════════════════════════════════════╝");
                SafeResetConsoleColor();
            }
            catch
            {
                // Ignore console layout issues on startup
            }

            try
            {
                int commandIndex = 0;
                if (args != null && args.Length > 0 && (args[0] == "--" || args[0] == "-") && args.Length > 1)
                {
                    commandIndex = 1;
                }

                if (args == null || args.Length == 0 || args[commandIndex].TrimStart('-', '—').ToLower() == "help" || args[commandIndex] == "-h" || args[commandIndex] == "--help")
                {
                    PrintHelp();
                    return 0;
                }

                string command = args[commandIndex].TrimStart('-', '—').ToLower();
                int skipCount = commandIndex + 1;
                string[] subcommandArgs = new string[args.Length - skipCount];
                Array.Copy(args, skipCount, subcommandArgs, 0, args.Length - skipCount);

                switch (command)
                {
                    case "validate":
                        return await RunValidateAsync(subcommandArgs).ConfigureAwait(false);
                    case "distill":
                        return await RunDistillAsync(subcommandArgs).ConfigureAwait(false);
                    case "repair":
                        return await RunRepairAsync(subcommandArgs).ConfigureAwait(false);
                    case "role":
                        return await RunRoleAsync(subcommandArgs).ConfigureAwait(false);
                    default:
                        SafeSetForegroundColor(ConsoleColor.Red);
                        Console.WriteLine($"[ERROR] Unknown command '{args[commandIndex]}'. Use 'help' to see list of valid commands.");
                        SafeResetConsoleColor();
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                SafeSetForegroundColor(ConsoleColor.Red);
                Console.WriteLine($"\n[ERROR] An unexpected startup error occurred: {ex.Message}");
                SafeResetConsoleColor();
                PrintHelp();
                return 1;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("\nUsage: dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- <command> [options]");
            Console.WriteLine("\nCommands:");
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  validate   Validates solution ZIP packages or source exported solutions against target metadata.");
            Console.WriteLine("  distill    Optimizes OOB bloated entities on server or repairs XML corruptions in local ZIPs.");
            Console.WriteLine("  repair     Parses a validation report and applies fixes (layer removal, dependency bundling).");
            Console.WriteLine("  role       Multi-environment user roles & business unit reporting and assignments.");
            Console.ResetColor();

            Console.WriteLine("\nGeneral Global Options:");
            Console.WriteLine("  --client-id <id>     Custom Azure AD Application (Client) ID.");
            Console.WriteLine("  --tenant <id>        Azure AD Tenant ID or name (default: organizations).");
            Console.WriteLine("  --redirect-uri <uri> Custom Redirect URI (default: http://localhost).");
            Console.WriteLine("  --login-hint <email> Pre-fills the login username (SSO hint).");

            Console.WriteLine("\nOptions for 'validate':");
            Console.WriteLine("  --zip <path>         Local path to solution ZIP file to validate.");
            Console.WriteLine("  --url <url>         Target environment Web API URL.");
            Console.WriteLine("  --connstr <str>     Target environment Connection String.");
            Console.WriteLine("  --interactive       Use interactive authentication for login.");
            Console.WriteLine("  --simulate          Run validation in offline simulation mode.");
            Console.WriteLine("  --out-json <path>   Output path for JSON report (default: validation_report.json).");
            Console.WriteLine("  --out-html <path>   Output path for HTML dashboard (default: validation_report.html).");
            Console.WriteLine("  --src-url <url>     Source environment Web API URL (for solution export/downloading).");
            Console.WriteLine("  --src-connstr <str> Source environment Connection String.");
            Console.WriteLine("  --solution <name>   Solution unique name to export from source environment.");
            Console.WriteLine("  --validation-log <path> Path to D365 XML/ZIP import error log to parse and merge.");

            Console.WriteLine("\nOptions for 'distill':");
            Console.WriteLine("  --url <url>         Source environment Web API URL (for direct-to-server distillation).");
            Console.WriteLine("  --solution <name>   Solution unique name to distill on server.");
            Console.WriteLine("  --zip <path>         Optional local solution ZIP path to check/repair XML corruptions.");
            Console.WriteLine("  --out-zip <path>     Output path for repaired ZIP file.");
            Console.WriteLine("  --simulate          Run in simulation mode.");
            Console.WriteLine("  --out-diff <path>   Output path for distillation diff ledger (default: distill_diff.json).");

            Console.WriteLine("\nOptions for 'repair':");
            Console.WriteLine("  --report <path>     Path to the generated JSON validation report to parse.");
            Console.WriteLine("  --url <url>         Target environment URL.");
            Console.WriteLine("  --connstr <str>     Target environment Connection String.");
            Console.WriteLine("  --src-url <url>     Source environment URL (for dependency additions).");
            Console.WriteLine("  --src-connstr <str> Source environment Connection String.");
            Console.WriteLine("  --solution <name>   Solution unique name.");
            Console.WriteLine("  --interactive       Use interactive authentication.");
            Console.WriteLine("  --simulate          Run in offline simulation mode.");

            Console.WriteLine("\nOptions for 'role' subcommands (report, audit, assign, remove):");
            Console.WriteLine("  --email <list>      Comma-separated list of target user emails/domainnames.");
            Console.WriteLine("  --role <list>       Security role name(s) (comma-separated for audits, single for assignments).");
            Console.WriteLine("  --bu <list>         Business Unit name(s) (comma-separated for audits, single for transfers).");
            Console.WriteLine("  --env <name>        Filter operations/reports to a single environment unique name.");
            Console.WriteLine("  --all               Run audit/reports across all discovered environments in tenant.");
            Console.WriteLine("  --simulate          Run in dry-run mode (print planned actions without executing).");
            Console.WriteLine("  --out-json <path>   Output JSON report path (default: user_role_report.json).");
            Console.WriteLine("  --out-html <path>   Output HTML report path (default: user_role_report.html).");
            Console.WriteLine();
        }

        private static async Task<int> RunValidateAsync(string[] args)
        {
            string zipPath = "";
            string envUrl = "https://simulation-env.crm.dynamics.com";
            string connString = "";
            bool interactive = true;
            bool simulate = false;
            string outJson = "validation_report.json";
            string outHtml = "validation_report.html";
            string srcUrl = "";
            string srcConnstr = "";
            string solutionName = "";
            string validationLog = "";
            string clientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
            string tenantId = "";
            string redirectUri = "http://localhost";
            string loginHint = "";

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Replace("—", "--").ToLower();
                switch (arg)
                {
                    case "--zip":
                    case "-zip":
                        if (i + 1 < args.Length) zipPath = args[++i];
                        break;
                    case "--url":
                    case "-url":
                        if (i + 1 < args.Length) envUrl = args[++i];
                        break;
                    case "--connstr":
                    case "-connstr":
                        if (i + 1 < args.Length) connString = args[++i];
                        break;
                    case "--interactive":
                    case "-interactive":
                        if (i + 1 < args.Length && (args[i + 1].ToLower() == "false" || args[i + 1].ToLower() == "true"))
                        {
                            interactive = bool.Parse(args[++i]);
                        }
                        else
                        {
                            interactive = true;
                        }
                        break;
                    case "--client-id":
                    case "-client-id":
                        if (i + 1 < args.Length) clientId = args[++i];
                        break;
                    case "--tenant":
                    case "-tenant":
                        if (i + 1 < args.Length) tenantId = args[++i];
                        break;
                    case "--redirect-uri":
                    case "-redirect-uri":
                        if (i + 1 < args.Length) redirectUri = args[++i];
                        break;
                    case "--login-hint":
                    case "-login-hint":
                        if (i + 1 < args.Length) loginHint = args[++i];
                        break;
                    case "--simulate":
                    case "-simulate":
                        simulate = true;
                        break;
                    case "--out-json":
                    case "-out-json":
                        if (i + 1 < args.Length) outJson = args[++i];
                        break;
                    case "--out-html":
                    case "-out-html":
                        if (i + 1 < args.Length) outHtml = args[++i];
                        break;
                    case "--src-url":
                    case "-src-url":
                        if (i + 1 < args.Length) srcUrl = args[++i];
                        break;
                    case "--src-connstr":
                    case "-src-connstr":
                        if (i + 1 < args.Length) srcConnstr = args[++i];
                        break;
                    case "--solution":
                    case "-solution":
                        if (i + 1 < args.Length) solutionName = args[++i];
                        break;
                    case "--validation-log":
                    case "-validation-log":
                        if (i + 1 < args.Length) validationLog = args[++i];
                        break;
                }
            }

            if (string.IsNullOrEmpty(zipPath) && string.IsNullOrEmpty(solutionName))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[INFO] No solution zip or solution name specified. Running Demo simulation...");
                Console.ResetColor();
                simulate = true;
                zipPath = CreateMockZipForDemo();
            }

            var targetProfile = new ConnectionProfile
            {
                EnvironmentUrl = envUrl,
                ConnectionString = connString,
                UseInteractiveAuth = interactive,
                ClientId = clientId,
                TenantId = tenantId,
                RedirectUri = redirectUri,
                LoginHint = loginHint,
                TimeoutSeconds = 60
            };

            ConnectionProfile? sourceProfile = null;
            if (!string.IsNullOrEmpty(srcUrl) || !string.IsNullOrEmpty(srcConnstr))
            {
                sourceProfile = new ConnectionProfile
                {
                    EnvironmentUrl = string.IsNullOrEmpty(srcUrl) ? "https://source-env.crm.dynamics.com" : srcUrl,
                    ConnectionString = srcConnstr,
                    UseInteractiveAuth = interactive,
                    ClientId = clientId,
                    TenantId = tenantId,
                    RedirectUri = redirectUri,
                    LoginHint = loginHint,
                    TimeoutSeconds = 60
                };
            }

            Console.WriteLine($"[Orchestrator] Initiating Validation...");
            Console.WriteLine($"  Target:       {targetProfile.EnvironmentUrl}");
            if (sourceProfile != null) Console.WriteLine($"  Source:       {sourceProfile.EnvironmentUrl} (Export solution: '{solutionName}')");
            Console.WriteLine($"  Simulation:   {simulate}");
            Console.WriteLine();

            var progress = new Progress<ProgressUpdate>(OnProgressReported);
            var orchestrator = new ValidationOrchestrator(useSimulationMode: simulate);

            try
            {
                var result = await orchestrator.ExecuteValidationAsync(
                    zipPath, targetProfile, outJson, outHtml, sourceProfile, solutionName, validationLog, progress
                ).ConfigureAwait(false);

                PrintValidationResult(result);
                PrintReportPaths(outJson, outHtml, result.IsSuccess);
                return result.IsSuccess ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Validation process failed: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                return 1;
            }
            finally
            {
                // cleanup demo zip
                if (args.Length == 0 && File.Exists(zipPath))
                {
                    try { File.Delete(zipPath); } catch { }
                }
            }
        }

        private static async Task<int> RunDistillAsync(string[] args)
        {
            string srcUrl = "";
            string solutionName = "";
            string zipPath = "";
            string outZipPath = "";
            bool simulate = false;
            string outDiff = "distill_diff.json";
            bool interactive = true;
            string clientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
            string tenantId = "";
            string redirectUri = "http://localhost";
            string loginHint = "";

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Replace("—", "--").ToLower();
                switch (arg)
                {
                    case "--url":
                    case "-url":
                        if (i + 1 < args.Length) srcUrl = args[++i];
                        break;
                    case "--solution":
                    case "-solution":
                        if (i + 1 < args.Length) solutionName = args[++i];
                        break;
                    case "--zip":
                    case "-zip":
                        if (i + 1 < args.Length) zipPath = args[++i];
                        break;
                    case "--out-zip":
                    case "-out-zip":
                        if (i + 1 < args.Length) outZipPath = args[++i];
                        break;
                    case "--simulate":
                    case "-simulate":
                        simulate = true;
                        break;
                    case "--out-diff":
                    case "-out-diff":
                        if (i + 1 < args.Length) outDiff = args[++i];
                        break;
                    case "--client-id":
                    case "-client-id":
                        if (i + 1 < args.Length) clientId = args[++i];
                        break;
                    case "--tenant":
                    case "-tenant":
                        if (i + 1 < args.Length) tenantId = args[++i];
                        break;
                    case "--redirect-uri":
                    case "-redirect-uri":
                        if (i + 1 < args.Length) redirectUri = args[++i];
                        break;
                    case "--login-hint":
                    case "-login-hint":
                        if (i + 1 < args.Length) loginHint = args[++i];
                        break;
                    case "--interactive":
                    case "-interactive":
                        if (i + 1 < args.Length && (args[i + 1].ToLower() == "false" || args[i + 1].ToLower() == "true"))
                        {
                            interactive = bool.Parse(args[++i]);
                        }
                        else
                        {
                            interactive = true;
                        }
                        break;
                }
            }

            var progress = new Progress<ProgressUpdate>(OnProgressReported);

            // Case A: Local Solution ZIP XML Repair mode
            if (!string.IsNullOrEmpty(zipPath))
            {
                if (string.IsNullOrEmpty(outZipPath)) outZipPath = Path.Combine(Path.GetDirectoryName(zipPath) ?? "", $"repaired_{Path.GetFileName(zipPath)}");
                
                Console.WriteLine($"[Distiller] Repairing XML corruptions in local solution package...");
                Console.WriteLine($"  Source ZIP: {Path.GetFullPath(zipPath)}");
                Console.WriteLine($"  Target ZIP: {Path.GetFullPath(outZipPath)}\n");

                try
                {
                    var pruner = new SolutionPruner(useSimulationMode: simulate);
                    await pruner.RepairZipXmlCorruptionsAsync(zipPath, outZipPath, progress).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[ERROR] XML Repair failed: {ex.Message}");
                    Console.ResetColor();
                    return 1;
                }
            }

            // Case B: Direct-to-Server Solution Distillation mode
            if (string.IsNullOrEmpty(solutionName))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[INFO] No solution name specified. Running Demo simulation...");
                Console.ResetColor();
                simulate = true;
                solutionName = "CustomerManagementExtension";
            }

            var sourceProfile = new ConnectionProfile
            {
                EnvironmentUrl = string.IsNullOrEmpty(srcUrl) ? "https://source-env.crm.dynamics.com" : srcUrl,
                UseInteractiveAuth = interactive,
                ClientId = clientId,
                TenantId = tenantId,
                RedirectUri = redirectUri,
                LoginHint = loginHint,
                TimeoutSeconds = 60
            };

            Console.WriteLine($"[Distiller] Distilling solution on server directly...");
            Console.WriteLine($"  Source environment URL: {sourceProfile.EnvironmentUrl}");
            Console.WriteLine($"  Solution Name:          {solutionName}");
            Console.WriteLine($"  Simulation mode:        {simulate}\n");

            try
            {
                var factory = new DataverseConnectionFactory();
                using var sourceClient = simulate ? null : factory.CreateHttpClient(sourceProfile);

                var distiller = new SolutionDistillerEngine(sourceClient, useSimulationMode: simulate);
                var report = await distiller.DistillSolutionAsync(solutionName, progress).ConfigureAwait(false);

                // Write diff ledger
                string jsonReport = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(outDiff, jsonReport).ConfigureAwait(false);

                PrintDistillSummary(report);
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Server distillation failed: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task<int> RunRepairAsync(string[] args)
        {
            string reportPath = "validation_report.json";
            string envUrl = "https://simulation-env.crm.dynamics.com";
            string connString = "";
            string srcUrl = "";
            string srcConnstr = "";
            string solutionName = "";
            bool interactive = true;
            bool simulate = false;
            string clientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
            string tenantId = "";
            string redirectUri = "http://localhost";
            string loginHint = "";

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Replace("—", "--").ToLower();
                switch (arg)
                {
                    case "--report":
                    case "-report":
                        if (i + 1 < args.Length) reportPath = args[++i];
                        break;
                    case "--url":
                    case "-url":
                        if (i + 1 < args.Length) envUrl = args[++i];
                        break;
                    case "--connstr":
                    case "-connstr":
                        if (i + 1 < args.Length) connString = args[++i];
                        break;
                    case "--src-url":
                    case "-src-url":
                        if (i + 1 < args.Length) srcUrl = args[++i];
                        break;
                    case "--src-connstr":
                    case "-src-connstr":
                        if (i + 1 < args.Length) srcConnstr = args[++i];
                        break;
                    case "--solution":
                    case "-solution":
                        if (i + 1 < args.Length) solutionName = args[++i];
                        break;
                    case "--interactive":
                    case "-interactive":
                        if (i + 1 < args.Length && (args[i + 1].ToLower() == "false" || args[i + 1].ToLower() == "true"))
                        {
                            interactive = bool.Parse(args[++i]);
                        }
                        else
                        {
                            interactive = true;
                        }
                        break;
                    case "--client-id":
                    case "-client-id":
                        if (i + 1 < args.Length) clientId = args[++i];
                        break;
                    case "--tenant":
                    case "-tenant":
                        if (i + 1 < args.Length) tenantId = args[++i];
                        break;
                    case "--redirect-uri":
                    case "-redirect-uri":
                        if (i + 1 < args.Length) redirectUri = args[++i];
                        break;
                    case "--login-hint":
                    case "-login-hint":
                        if (i + 1 < args.Length) loginHint = args[++i];
                        break;
                    case "--simulate":
                    case "-simulate":
                        simulate = true;
                        break;
                }
            }

            if (!File.Exists(reportPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Validation report JSON not found at: {reportPath}. Please run 'validate' first to generate the report.");
                Console.ResetColor();
                return 1;
            }

            var targetProfile = new ConnectionProfile
            {
                EnvironmentUrl = envUrl,
                ConnectionString = connString,
                UseInteractiveAuth = interactive,
                ClientId = clientId,
                TenantId = tenantId,
                RedirectUri = redirectUri,
                LoginHint = loginHint,
                TimeoutSeconds = 60
            };

            ConnectionProfile? sourceProfile = null;
            if (!string.IsNullOrEmpty(srcUrl) || !string.IsNullOrEmpty(srcConnstr))
            {
                sourceProfile = new ConnectionProfile
                {
                    EnvironmentUrl = srcUrl,
                    ConnectionString = srcConnstr,
                    UseInteractiveAuth = interactive,
                    ClientId = clientId,
                    TenantId = tenantId,
                    RedirectUri = redirectUri,
                    LoginHint = loginHint,
                    TimeoutSeconds = 60
                };
            }

            Console.WriteLine($"[Repairer] Initiating programmatic repairs based on report: {reportPath}...");
            Console.WriteLine($"  Target:       {targetProfile.EnvironmentUrl}");
            if (sourceProfile != null) Console.WriteLine($"  Source:       {sourceProfile.EnvironmentUrl} (Solution: '{solutionName}')");
            Console.WriteLine($"  Simulation:   {simulate}");
            Console.WriteLine();

            var progress = new Progress<ProgressUpdate>(OnProgressReported);
            var repairer = new SolutionRepairer(useSimulationMode: simulate);

            try
            {
                int repairedCount = await repairer.RepairSolutionAsync(
                    reportPath, targetProfile, sourceProfile, solutionName, progress
                ).ConfigureAwait(false);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[SUCCESS] Repair workflow completed. programmatically resolved {repairedCount} issues.");
                Console.ResetColor();
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Repair execution failed: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static void OnProgressReported(ProgressUpdate update)
        {
            // Select color based on status
            ConsoleColor originalColor = Console.ForegroundColor;
            switch (update.Status)
            {
                case ProgressStatus.Success:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("[OK]   ");
                    break;
                case ProgressStatus.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("[WARN] ");
                    break;
                case ProgressStatus.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("[FAIL] ");
                    break;
                case ProgressStatus.Info:
                default:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("[INFO] ");
                    break;
            }
            Console.ResetColor();

            string pctStr = update.PercentComplete >= 0 ? $" {update.PercentComplete:0}%" : "";
            Console.WriteLine($"[{update.Stage}]{pctStr} {update.Message}");
        }

        private static string CreateMockZipForDemo()
        {
            string path = Path.Combine(Path.GetTempPath(), "DemoSolution.zip");
            using (var fs = new FileStream(path, FileMode.Create))
            {
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var solEl = archive.CreateEntry("solution.xml");
                    using (var sw = new StreamWriter(solEl.Open()))
                    {
                        sw.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<ImportExportXml version=""9.2.0.0"">
  <SolutionManifest>
    <UniqueName>EnterpriseCoreModifications</UniqueName>
    <Version>2.4.0.0</Version>
    <Managed>1</Managed>
    <RootComponents>
      <RootComponent type=""60"" id=""F001-GUID-MISSING-ATTR"" schemaName=""Account Main Form"" />
      <RootComponent type=""62"" id=""SITEMAP-MISSING-ENT"" schemaName=""SiteMap"" />
      <RootComponent type=""63"" id=""RIBBON-MISSING-WR"" schemaName=""Ribbon"" />
    </RootComponents>
  </SolutionManifest>
  <MissingDependencies>
    <MissingDependency>
      <Required type=""61"" schemaName=""custom_library.js"" displayName=""Custom JavaScript Helper"" solution=""CorePrerequisites"" />
      <Dependent type=""60"" schemaName=""Account Main Form"" displayName=""Account Main Form"" />
    </MissingDependency>
    <MissingDependency>
      <Required type=""80"" schemaName=""msdyn_SalesBase"" displayName=""D365 Sales Base Solution"" solution=""Sales"" />
      <Dependent type=""60"" schemaName=""Account Main Form"" displayName=""Account Main Form"" />
    </MissingDependency>
  </MissingDependencies>
</ImportExportXml>");
                    }

                    var custEl = archive.CreateEntry("customizations.xml");
                    using (var sw = new StreamWriter(custEl.Open()))
                    {
                        sw.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<ImportExportXml>
  <Entities>
    <Entity Name=""opportunity"">
      <EntityInfo>
        <Attributes>
          <Attribute PhysicalName=""new_transactionamount"">
            <Type>decimal</Type>
            <Length>2</Length>
          </Attribute>
        </Attributes>
      </EntityInfo>
    </Entity>
  </Entities>
</ImportExportXml>");
                    }
                }
            }
            return path;
        }

        private static void PrintValidationResult(ValidationResult result)
        {
            var r = result.Report;
            Console.WriteLine("\n=================================================================");
            Console.WriteLine("                       VALIDATION SUMMARY                        ");
            Console.WriteLine("=================================================================");
            
            Console.Write("  Overall Status:    ");
            if (result.IsSuccess)
            {
                if (r.Metrics.WarningsCount > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[PASSED WITH WARNINGS]");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[PASSED]");
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAILED]");
            }
            Console.ResetColor();

            Console.WriteLine($"  Confidence Score:  {r.ConfidenceScore}");
            Console.WriteLine($"  Total Evaluated:   {r.Metrics.TotalComponentsEvaluated}");
            Console.WriteLine($"  Blockers (Red):    {r.Metrics.BlockersCount}");
            Console.WriteLine($"  Warnings (Yellow):  {r.Metrics.WarningsCount}");
            Console.WriteLine("-----------------------------------------------------------------");

            if (r.Issues.Count > 0)
            {
                Console.WriteLine("  Active Issues List:");
                foreach (var issue in r.Issues)
                {
                    if (issue.Severity == "Red")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write("  [BLOCKER] ");
                    }
                    else if (issue.Severity == "Yellow")
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("  [WARNING] ");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write("  [INFO]    ");
                    }
                    Console.ResetColor();

                    Console.WriteLine($"{issue.Id} - ({issue.ComponentType}) {issue.LogicalName}:");
                    Console.WriteLine($"            {issue.Description}");
                }
            }
            
            if (r.MetadataGaps.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("\n  Metadata Gaps (Incomplete Target Crawls):");
                Console.ResetColor();
                foreach (var gap in r.MetadataGaps)
                {
                    Console.WriteLine($"  - {gap}");
                }
            }
            Console.WriteLine("=================================================================");
        }

        private static void PrintDistillSummary(DistillerReport report)
        {
            Console.WriteLine("\n=================================================================");
            Console.WriteLine("                       DISTILLATION SUMMARY                      ");
            Console.WriteLine("=================================================================");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Status:                COMPLETED DIRECT-ON-SERVER [COMPLETED]");
            Console.ResetColor();
            Console.WriteLine($"  Solution Name:         {report.SolutionName}");
            Console.WriteLine($"  Optimized Components:  {report.ComponentsRemoved.Count}");
            Console.WriteLine("-----------------------------------------------------------------");
            if (report.ComponentsRemoved.Count > 0)
            {
                Console.WriteLine("  Distilled component list:");
                foreach (var comp in report.ComponentsRemoved)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("  [OPTIMIZED] ");
                    Console.ResetColor();
                    Console.WriteLine($"{comp.LogicalName} ({comp.Type}) - {comp.Reason}");
                }
            }
            Console.WriteLine("=================================================================");
        }

        private static void PrintReportPaths(string jsonPath, string htmlPath, bool isSuccess)
        {
            Console.WriteLine("=================================================================");
            Console.Write("Result:              ");
            if (isSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("VALIDATION COMPLETED SUCCESSFULLY");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("VALIDATION FAILED");
            }
            Console.ResetColor();
            
            Console.WriteLine("\nReports generated. Copy a path below and open it in a browser:");
            try
            {
                Console.WriteLine($"HTML Report: {Path.GetFullPath(htmlPath)}");
                Console.WriteLine($"JSON Report: {Path.GetFullPath(jsonPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTML Report: {htmlPath} (error resolving path: {ex.Message})");
                Console.WriteLine($"JSON Report: {jsonPath} (error resolving path: {ex.Message})");
            }
            Console.WriteLine("=================================================================");
        }

        private static async Task<int> RunRoleAsync(string[] args)
        {
            string subAction = "";
            List<string> emails = new();
            List<string> roles = new();
            List<string> bus = new();
            string envFilter = "";
            bool allEnvs = false;
            bool simulate = false;
            string outHtml = "";
            string outJson = "";
            bool interactive = true;
            string envUrl = "";
            string connString = "";
            string clientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
            string tenantId = "";
            string redirectUri = "http://localhost";
            string loginHint = "";

            if (args.Length > 0)
            {
                subAction = args[0].ToLower();
            }

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i].Replace("—", "--").ToLower();
                switch (arg)
                {
                    case "--email":
                    case "-email":
                    case "--emails":
                    case "-emails":
                        if (i + 1 < args.Length)
                        {
                            emails.AddRange(args[++i].Split(',').Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e)));
                        }
                        break;
                    case "--role":
                    case "-role":
                    case "--roles":
                    case "-roles":
                        if (i + 1 < args.Length)
                        {
                            roles.AddRange(args[++i].Split(',').Select(r => r.Trim()).Where(r => !string.IsNullOrEmpty(r)));
                        }
                        break;
                    case "--bu":
                    case "-bu":
                    case "--bus":
                    case "-bus":
                        if (i + 1 < args.Length)
                        {
                            bus.AddRange(args[++i].Split(',').Select(b => b.Trim()).Where(b => !string.IsNullOrEmpty(b)));
                        }
                        break;
                    case "--env":
                    case "-env":
                    case "--envs":
                    case "-envs":
                        if (i + 1 < args.Length)
                        {
                            envFilter = args[++i].Trim();
                        }
                        break;
                    case "--all":
                    case "-all":
                        allEnvs = true;
                        break;
                    case "--simulate":
                    case "-simulate":
                        simulate = true;
                        break;
                    case "--out-html":
                    case "-out-html":
                        if (i + 1 < args.Length) outHtml = args[++i];
                        break;
                    case "--out-json":
                    case "-out-json":
                        if (i + 1 < args.Length) outJson = args[++i];
                        break;
                    case "--url":
                    case "-url":
                        if (i + 1 < args.Length) envUrl = args[++i];
                        break;
                    case "--connstr":
                    case "-connstr":
                        if (i + 1 < args.Length) connString = args[++i];
                        break;
                    case "--interactive":
                    case "-interactive":
                        if (i + 1 < args.Length && (args[i + 1].ToLower() == "false" || args[i + 1].ToLower() == "true"))
                        {
                            interactive = bool.Parse(args[++i]);
                        }
                        else
                        {
                            interactive = true;
                        }
                        break;
                    case "--client-id":
                    case "-client-id":
                        if (i + 1 < args.Length) clientId = args[++i];
                        break;
                    case "--tenant":
                    case "-tenant":
                        if (i + 1 < args.Length) tenantId = args[++i];
                        break;
                    case "--redirect-uri":
                    case "-redirect-uri":
                        if (i + 1 < args.Length) redirectUri = args[++i];
                        break;
                    case "--login-hint":
                    case "-login-hint":
                        if (i + 1 < args.Length) loginHint = args[++i];
                        break;
                }
            }

            if (string.IsNullOrEmpty(envUrl) && string.IsNullOrEmpty(connString))
            {
                simulate = true;
                envUrl = "https://simulation-env.crm.dynamics.com";
            }

            var profile = new ConnectionProfile
            {
                EnvironmentUrl = envUrl,
                ConnectionString = connString,
                UseInteractiveAuth = interactive,
                ClientId = clientId,
                TenantId = tenantId,
                RedirectUri = redirectUri,
                LoginHint = loginHint,
                TimeoutSeconds = 60
            };

            var disco = new EnvironmentDiscovery();
            var crawler = new UserRoleCrawler();
            var assignmentEngine = new RoleAssignmentEngine();
            var comparer = new RoleComparer();

            Action<string> log = msg =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("[ROLE] ");
                Console.ResetColor();
                Console.WriteLine(msg);
            };

            try
            {
                var discovered = await disco.DiscoverEnvironmentsAsync(profile, log).ConfigureAwait(false);
                if (discovered == null || discovered.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] No target environments discovered or resolved. Execution aborted.");
                    Console.ResetColor();
                    return 1;
                }

                List<InstanceDto> targets;
                if (!allEnvs && !string.IsNullOrEmpty(envFilter))
                {
                    targets = discovered.Where(d => d.UniqueName.Equals(envFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (targets.Count == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[WARNING] Filter environment '{envFilter}' was not found. Using all discovered environments.");
                        Console.ResetColor();
                        targets = discovered;
                    }
                }
                else
                {
                    targets = discovered;
                }

                log($"Target environment scope: {string.Join(", ", targets.Select(t => t.UniqueName))}");

                switch (subAction)
                {
                    case "report":
                        if (emails.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[ERROR] '--email' must be specified for 'report' subcommand.");
                            Console.ResetColor();
                            return 1;
                        }
                        if (string.IsNullOrEmpty(outHtml)) outHtml = "user_role_report.html";
                        if (string.IsNullOrEmpty(outJson)) outJson = "user_role_report.json";

                        var userReport = await crawler.CrawlUsersAsync(targets, profile, emails, log).ConfigureAwait(false);
                        comparer.ExportReportToJson(outJson, userReport);
                        comparer.ExportUserRoleReportToHtml(outHtml, userReport);

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("\n[SUCCESS] User role and BU report completed successfully.");
                        Console.ResetColor();
                        PrintPaths(outJson, outHtml);
                        break;

                    case "audit":
                        if (roles.Count == 0 && bus.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[ERROR] Either '--role' or '--bu' must be specified for 'audit' subcommand.");
                            Console.ResetColor();
                            return 1;
                        }

                        if (roles.Count > 0)
                        {
                            if (string.IsNullOrEmpty(outHtml)) outHtml = "role_audit_report.html";
                            if (string.IsNullOrEmpty(outJson)) outJson = "role_audit_report.json";

                            var roleReport = await crawler.AuditRolesAsync(targets, profile, roles, log).ConfigureAwait(false);
                            comparer.ExportReportToJson(outJson, roleReport);
                            comparer.ExportRoleAuditReportToHtml(outHtml, roleReport);

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("\n[SUCCESS] Security role compliance audit completed successfully.");
                            Console.ResetColor();
                            PrintPaths(outJson, outHtml);
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(outHtml)) outHtml = "bu_audit_report.html";
                            if (string.IsNullOrEmpty(outJson)) outJson = "bu_audit_report.json";

                            var buReport = await crawler.AuditBusAsync(targets, profile, bus, log).ConfigureAwait(false);
                            comparer.ExportReportToJson(outJson, buReport);
                            comparer.ExportBuAuditReportToHtml(outHtml, buReport);

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("\n[SUCCESS] Business Unit compliance audit completed successfully.");
                            Console.ResetColor();
                            PrintPaths(outJson, outHtml);
                        }
                        break;

                    case "assign":
                        if (emails.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[ERROR] '--email' must be specified.");
                            Console.ResetColor();
                            return 1;
                        }
                        if (roles.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[ERROR] '--role' must be specified.");
                            Console.ResetColor();
                            return 1;
                        }

                        string targetRole = roles.First();
                        if (bus.Count > 0)
                        {
                            string targetBu = bus.First();
                            await assignmentEngine.SetBusinessUnitAsync(targets, profile, emails, targetBu, targetRole, simulate, log).ConfigureAwait(false);
                        }
                        else
                        {
                            await assignmentEngine.AssignRoleAsync(targets, profile, emails, targetRole, simulate, log).ConfigureAwait(false);
                        }

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n[SUCCESS] Role assignment operation completed.");
                        Console.ResetColor();
                        break;

                    case "remove":
                        if (emails.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[ERROR] '--email' must be specified.");
                            Console.ResetColor();
                            return 1;
                        }
                        if (roles.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[ERROR] '--role' must be specified.");
                            Console.ResetColor();
                            return 1;
                        }

                        string roleToRemove = roles.First();
                        await assignmentEngine.RemoveRoleAsync(targets, profile, emails, roleToRemove, simulate, log).ConfigureAwait(false);

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n[SUCCESS] Role removal operation completed.");
                        Console.ResetColor();
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[ERROR] Unknown role sub-action '{subAction}'. Valid actions: report, audit, assign, remove.");
                        Console.ResetColor();
                        return 1;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Role subcommand failed: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static void PrintPaths(string jsonPath, string htmlPath)
        {
            Console.WriteLine("=================================================================");
            Console.WriteLine("Reports generated. Copy a path below and open it in a browser:");
            try
            {
                Console.WriteLine($"HTML Report: {Path.GetFullPath(htmlPath)}");
                Console.WriteLine($"JSON Report: {Path.GetFullPath(jsonPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTML Report: {htmlPath} (error resolving path: {ex.Message})");
                Console.WriteLine($"JSON Report: {jsonPath} (error resolving path: {ex.Message})");
            }
            Console.WriteLine("=================================================================");
        }
    }
}
