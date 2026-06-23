using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerPlatform.ProductivityEngine.Core.Reporting
{
    public class HtmlReportGenerator : IReportExporter
    {
        public async Task ExportValidationReportAsync(ValidationReport report, string outputJsonPath, string outputHtmlPath)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            // 1. Export JSON
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            string jsonContent = JsonSerializer.Serialize(report, jsonOptions);
            
            string jsonDir = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrEmpty(jsonDir)) Directory.CreateDirectory(jsonDir);
            await File.WriteAllTextAsync(outputJsonPath, jsonContent, Encoding.UTF8).ConfigureAwait(false);

            // 2. Generate HTML
            string htmlContent = GenerateHtmlReport(report);
            string htmlDir = Path.GetDirectoryName(outputHtmlPath);
            if (!string.IsNullOrEmpty(htmlDir)) Directory.CreateDirectory(htmlDir);
            await File.WriteAllTextAsync(outputHtmlPath, htmlContent, Encoding.UTF8).ConfigureAwait(false);
        }

        public async Task ExportDistillerReportAsync(DistillerReport report, string outputJsonPath)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            string jsonContent = JsonSerializer.Serialize(report, jsonOptions);
            
            string jsonDir = Path.GetDirectoryName(outputJsonPath);
            if (!string.IsNullOrEmpty(jsonDir)) Directory.CreateDirectory(jsonDir);
            await File.WriteAllTextAsync(outputJsonPath, jsonContent, Encoding.UTF8).ConfigureAwait(false);
        }

        private string GenerateHtmlReport(ValidationReport report)
        {
            // Determine styling variables based on state
            string statusColor;
            string statusText;
            string statusBannerBg;

            if (report.Metrics.BlockersCount > 0)
            {
                statusColor = "#ff6b7a"; // Crimson Red
                statusText = "FAILED (BLOCKERS DETECTED)";
                statusBannerBg = "linear-gradient(135deg, #721c24 0%, #dc3545 100%)";
            }
            else if (report.Metrics.WarningsCount > 0)
            {
                statusColor = "#ffd54f"; // Amber Yellow
                statusText = "PASSED WITH WARNINGS";
                statusBannerBg = "linear-gradient(135deg, #856404 0%, #e0a800 100%)";
            }
            else
            {
                statusColor = "#34d399"; // Success Green
                statusText = "SUCCESS (NO ISSUES DETECTED)";
                statusBannerBg = "linear-gradient(135deg, #0f5132 0%, #198754 100%)";
            }

            var mainSb = new StringBuilder();
            var infoSb = new StringBuilder();
            int mainCount = 0;
            int infoCount = 0;

            foreach (var issue in report.Issues)
            {
                bool isInfo = issue.Severity.Equals("Info", StringComparison.OrdinalIgnoreCase);
                var currentSb = isInfo ? infoSb : mainSb;
                if (isInfo) infoCount++; else mainCount++;

                string severityBadgeClass = "badge-info";
                if (issue.Severity.Equals("Red", StringComparison.OrdinalIgnoreCase))
                {
                    severityBadgeClass = "badge-danger";
                }
                else if (issue.Severity.Equals("Yellow", StringComparison.OrdinalIgnoreCase))
                {
                    severityBadgeClass = "badge-warning";
                }

                string resolutionLink = !string.IsNullOrEmpty(issue.ResolutionUrl)
                    ? $" <a href='{issue.ResolutionUrl}' target='_blank' class='resolution-link'>[Learn More]</a>"
                    : "";

                string odataQuerySection = !string.IsNullOrEmpty(issue.HelpODataQuery)
                    ? $@"<div class='odata-box'>
                            <span class='odata-label'>Verification Query:</span>
                            <code>{issue.HelpODataQuery}</code>
                         </div>"
                    : "";

                string logicalNameDisplay = !string.IsNullOrEmpty(issue.ParentTable)
                    ? $"{issue.ParentTable} : {issue.LogicalName}"
                    : issue.LogicalName;

                currentSb.Append($@"
                    <tr class='issue-row' data-severity='{issue.Severity}'>
                        <td><span class='badge {severityBadgeClass}'>{issue.Severity}</span></td>
                        <td class='font-monospace'>{issue.Id}</td>
                        <td>{issue.ComponentType}</td>
                        <td class='font-monospace'>{logicalNameDisplay}</td>
                        <td>
                            <div class='issue-desc'>{issue.Description}{resolutionLink}</div>
                            {odataQuerySection}
                        </td>
                    </tr>");
            }

            string tableRowsHtml = mainSb.ToString();
            if (mainCount == 0)
            {
                tableRowsHtml = @"
                    <tr>
                        <td colspan='5' class='text-center py-5 text-muted'>
                            <div class='fs-4 mb-2'>🎉 No Blockers or Warnings Found!</div>
                            Your solution has passed all critical checks.
                        </td>
                    </tr>";
            }

            string infoTableRowsHtml = infoSb.ToString();
            if (infoCount == 0)
            {
                infoTableRowsHtml = @"
                    <tr>
                        <td colspan='5' class='text-center py-4 text-muted'>
                            No informational logs generated.
                        </td>
                    </tr>";
            }

            string infoSectionHtml = "";
            if (infoCount > 0)
            {
                infoSectionHtml = $@"
        <!-- Info Severity Issues (Collapsible) -->
        <details class=""info-details"" style=""margin-top: 2rem; background-color: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: 12px; padding: 1.25rem 1.5rem;"">
            <summary style=""cursor: pointer; font-weight: 600; color: var(--text-secondary); outline: none; user-select: none;"">
                Additional Informational Logs ({infoCount})
            </summary>
            <div style=""margin-top: 1rem;"">
                <div class=""table-responsive"">
                    <table id=""infoIssuesTable"">
                        <thead>
                            <tr>
                                <th onclick=""sortInfoTable(0)"">Severity</th>
                                <th onclick=""sortInfoTable(1)"">Issue ID</th>
                                <th onclick=""sortInfoTable(2)"">Type</th>
                                <th onclick=""sortInfoTable(3)"">Logical Name</th>
                                <th onclick=""sortInfoTable(4)"">Description</th>
                            </tr>
                        </thead>
                        <tbody id=""infoTableBody"">
                            {infoTableRowsHtml}
                        </tbody>
                    </table>
                </div>
            </div>
        </details>";
            }

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Solution Deep Validator Report - {report.SolutionName}</title>
    <style>
        :root {{
            --bg-primary: #0f172a;
            --bg-secondary: #1e293b;
            --bg-tertiary: #334155;
            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --border-color: #334155;
            --crimson: #dc3545;
            --amber: #ffc107;
            --emerald: #198754;
            --transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
        }}

        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}

        body {{
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif;
            background-color: var(--bg-primary);
            color: var(--text-primary);
            line-height: 1.5;
            padding: 2.5rem 1.5rem;
        }}

        .container {{
            max-width: 1200px;
            margin: 0 auto;
        }}

        /* Header / Banner */
        .banner {{
            background: {statusBannerBg};
            border-radius: 16px;
            padding: 2.5rem;
            margin-bottom: 2rem;
            box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.3);
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: 1.5rem;
            position: relative;
            overflow: hidden;
        }}

        .banner::before {{
            content: '';
            position: absolute;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: linear-gradient(45deg, rgba(255,255,255,0.05) 0%, rgba(255,255,255,0) 100%);
            pointer-events: none;
        }}

        .banner-info h1 {{
            font-size: 2.25rem;
            font-weight: 700;
            letter-spacing: -0.025em;
            margin-bottom: 0.5rem;
            text-shadow: 0 2px 4px rgba(0,0,0,0.2);
        }}

        .banner-info p {{
            color: rgba(255, 255, 255, 0.8);
            font-size: 1.1rem;
        }}

        .meta-grid {{
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 0.75rem 1.5rem;
            margin-top: 1.25rem;
            max-width: 900px;
        }}

        @media (max-width: 768px) {{
            .meta-grid {{
                grid-template-columns: 1fr;
            }}
        }}

        .meta-item {{
            display: flex;
            flex-direction: column;
            gap: 0.15rem;
        }}

        .meta-label {{
            font-size: 0.75rem;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            color: rgba(255, 255, 255, 0.6);
            font-weight: 600;
        }}

        .meta-value {{
            font-size: 0.95rem;
            color: rgba(255, 255, 255, 0.95);
            font-weight: 500;
        }}

        .status-badge {{
            background: rgba(255, 255, 255, 0.15);
            backdrop-filter: blur(8px);
            border: 1px solid rgba(255, 255, 255, 0.2);
            padding: 0.75rem 1.5rem;
            border-radius: 9999px;
            font-weight: 700;
            font-size: 0.9rem;
            letter-spacing: 0.05em;
            text-transform: uppercase;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}

        /* Metrics Grid */
        .metrics-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 1.5rem;
            margin-bottom: 2rem;
        }}

        .metric-card {{
            background-color: var(--bg-secondary);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 1.5rem;
            display: flex;
            flex-direction: column;
            justify-content: space-between;
            transition: var(--transition);
            box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
        }}

        .metric-card:hover {{
            transform: translateY(-4px);
            border-color: var(--bg-tertiary);
            box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.2);
        }}

        .metric-title {{
            color: var(--text-secondary);
            font-size: 0.875rem;
            font-weight: 500;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            margin-bottom: 0.5rem;
        }}

        .metric-value {{
            font-size: 2rem;
            font-weight: 700;
        }}

        .metric-value.blockers {{ color: var(--crimson); }}
        .metric-value.warnings {{ color: var(--amber); }}
        .metric-value.evaluated {{ color: var(--text-primary); }}

        /* Filter Controls */
        .controls-card {{
            background-color: var(--bg-secondary);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 1.25rem 1.5rem;
            margin-bottom: 2rem;
            display: flex;
            align-items: center;
            justify-content: space-between;
            flex-wrap: wrap;
            gap: 1rem;
        }}

        .search-wrapper {{
            position: relative;
            flex: 1;
            max-width: 450px;
            min-width: 250px;
        }}

        .search-input {{
            width: 100%;
            background-color: var(--bg-primary);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 0.75rem 1rem;
            color: var(--text-primary);
            font-size: 0.95rem;
            transition: var(--transition);
        }}

        .search-input:focus {{
            outline: none;
            border-color: #6366f1;
            box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.25);
        }}

        .filter-buttons {{
            display: flex;
            gap: 0.5rem;
        }}

        .filter-btn {{
            background-color: var(--bg-primary);
            border: 1px solid var(--border-color);
            color: var(--text-secondary);
            padding: 0.6rem 1.2rem;
            border-radius: 8px;
            font-size: 0.9rem;
            font-weight: 500;
            cursor: pointer;
            transition: var(--transition);
        }}

        .filter-btn:hover, .filter-btn.active {{
            background-color: var(--bg-tertiary);
            color: var(--text-primary);
            border-color: var(--text-secondary);
        }}

        /* Table Structure */
        .table-card {{
            background-color: var(--bg-secondary);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
        }}

        .table-responsive {{
            overflow-x: auto;
        }}

        table {{
            width: 100%;
            border-collapse: collapse;
            text-align: left;
        }}

        th, td {{
            padding: 1.1rem 1.5rem;
            border-bottom: 1px solid var(--border-color);
        }}

        th {{
            background-color: rgba(15, 23, 42, 0.4);
            color: var(--text-secondary);
            font-weight: 600;
            font-size: 0.85rem;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            cursor: pointer;
            user-select: none;
        }}

        th:hover {{
            color: var(--text-primary);
        }}

        tr:last-child td {{
            border-bottom: none;
        }}

        /* Badges */
        .badge {{
            display: inline-flex;
            align-items: center;
            padding: 0.25rem 0.75rem;
            border-radius: 9999px;
            font-size: 0.75rem;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }}

        .badge-danger {{
            background-color: rgba(220, 53, 69, 0.15);
            color: #ff6b7a;
            border: 1px solid rgba(220, 53, 69, 0.3);
        }}

        .badge-warning {{
            background-color: rgba(255, 193, 7, 0.15);
            color: #ffd54f;
            border: 1px solid rgba(255, 193, 7, 0.3);
        }}

        .badge-info {{
            background-color: rgba(59, 130, 246, 0.15);
            color: #60a5fa;
            border: 1px solid rgba(59, 130, 246, 0.3);
        }}

        /* Formatting */
        .font-monospace, code {{
            font-family: SFMono-Regular, Consolas, ""Liberation Mono"", Menlo, Courier, monospace;
            font-size: 0.875rem;
        }}

        .issue-desc {{
            font-size: 0.95rem;
            margin-bottom: 0.5rem;
        }}

        .odata-box {{
            background-color: var(--bg-primary);
            border: 1px solid var(--border-color);
            border-radius: 6px;
            padding: 0.75rem;
            margin-top: 0.5rem;
            font-family: SFMono-Regular, Consolas, ""Liberation Mono"", Menlo, Courier, monospace;
            font-size: 0.8rem;
            overflow-x: auto;
        }}

        .odata-label {{
            display: block;
            font-size: 0.7rem;
            color: var(--text-secondary);
            text-transform: uppercase;
            letter-spacing: 0.05em;
            margin-bottom: 0.25rem;
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
            font-weight: 600;
        }}

        .resolution-link {{
            color: #818cf8;
            text-decoration: none;
            font-weight: 600;
            font-size: 0.9rem;
            margin-left: 0.5rem;
            transition: var(--transition);
        }}

        .resolution-link:hover {{
            color: #a5b4fc;
            text-decoration: underline;
        }}

        .text-center {{ text-align: center; }}
        .py-5 {{ padding-top: 3rem; padding-bottom: 3rem; }}
        .py-4 {{ padding-top: 2rem; padding-bottom: 2rem; }}
        .fs-4 {{ font-size: 1.5rem; }}
        .mb-2 {{ margin-bottom: 0.5rem; }}
        .text-muted {{ color: var(--text-secondary); }}

        @media (max-width: 768px) {{
            .banner {{
                padding: 1.5rem;
                flex-direction: column;
                align-items: flex-start;
            }}
            
            th, td {{
                padding: 0.8rem 1rem;
            }}
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <!-- Banner Dashboard -->
        <div class=""banner"">
            <div class=""banner-info"">
                <h1>{report.SolutionName}</h1>
                <p style=""font-size: 1.15rem; margin-bottom: 0.75rem; font-weight: 500;"">
                    Status: <span style=""font-weight: 700; color: #ffffff;"">{statusText}</span>
                </p>
                
                <div class=""meta-grid"">
                    <div class=""meta-item"">
                        <span class=""meta-label"">Target Env</span>
                        <span class=""meta-value"">{report.TargetFriendlyName} ({report.TargetEnvironment})</span>
                    </div>
                    <div class=""meta-item"">
                        <span class=""meta-label"">Solution Version</span>
                        <span class=""meta-value"">{report.SourceVersion}</span>
                    </div>
                    <div class=""meta-item"">
                        <span class=""meta-label"">Source Package</span>
                        <span class=""meta-value"" style=""word-break: break-all;"">{report.SourceZipPath}</span>
                    </div>
                    <div class=""meta-item"">
                        <span class=""meta-label"">Executed By</span>
                        <span class=""meta-value"">{report.UserPrincipalName}</span>
                    </div>
                    <div class=""meta-item"">
                        <span class=""meta-label"">Validation Period</span>
                        <span class=""meta-value"">{report.ValidationStartTimestamp:yyyy-MM-dd HH:mm:ss} to {report.ValidationEndTimestamp:yyyy-MM-dd HH:mm:ss} UTC ({report.ValidationDurationSeconds:F1}s)</span>
                    </div>
                </div>
            </div>
            <div class=""status-badge"" style=""color: {statusColor};"">
                {report.OverallResult}
            </div>
        </div>

        <!-- Metrics Cards -->
        <div class=""metrics-grid"">
            <div class=""metric-card"">
                <div class=""metric-title"">Components Scanned</div>
                <div class=""metric-value evaluated"">{report.Metrics.TotalComponentsEvaluated}</div>
            </div>
            <div class=""metric-card"">
                <div class=""metric-title"">Blockers (Red)</div>
                <div class=""metric-value blockers"">{report.Metrics.BlockersCount}</div>
            </div>
            <div class=""metric-card"">
                <div class=""metric-title"">Warnings (Yellow)</div>
                <div class=""metric-value warnings"">{report.Metrics.WarningsCount}</div>
            </div>
            <div class=""metric-card"">
                <div class=""metric-title"">Execution Timestamp</div>
                <div class=""metric-value"" style=""font-size: 1.15rem; font-weight: 500; margin-top: 0.5rem;"">
                    {report.ValidationEndTimestamp:yyyy-MM-dd HH:mm:ss} UTC
                </div>
            </div>
        </div>

        <!-- Controls -->
        <div class=""controls-card"">
            <div class=""search-wrapper"">
                <input type=""text"" id=""searchInput"" class=""search-input"" placeholder=""Search issues by ID, type, component name, or description..."">
            </div>
            <div class=""filter-buttons"">
                <button class=""filter-btn active"" onclick=""filterSeverity('All', this)"">Issues Raised ({mainCount})</button>
                <button class=""filter-btn"" onclick=""filterSeverity('Red', this)"">Blockers ({report.Metrics.BlockersCount})</button>
                <button class=""filter-btn"" onclick=""filterSeverity('Yellow', this)"">Warnings ({report.Metrics.WarningsCount})</button>
            </div>
        </div>

        <!-- Issues Ledger Table -->
        <div class=""table-card"">
            <div class=""table-responsive"">
                <table id=""issuesTable"">
                    <thead>
                        <tr>
                            <th onclick=""sortTable(0)"">Severity</th>
                            <th onclick=""sortTable(1)"">Issue ID</th>
                            <th onclick=""sortTable(2)"">Type</th>
                            <th onclick=""sortTable(3)"">Logical Name</th>
                            <th onclick=""sortTable(4)"">Description</th>
                        </tr>
                    </thead>
                    <tbody id=""tableBody"">
                        {tableRowsHtml}
                    </tbody>
                </table>
            </div>
        </div>

        {infoSectionHtml}
    </div>

    <script>
        // Filtering Logic
        let currentSeverityFilter = 'All';
        const searchInput = document.getElementById('searchInput');

        searchInput.addEventListener('input', applyFilters);

        function filterSeverity(severity, element) {{
            // Toggle active class
            document.querySelectorAll('.filter-btn').forEach(btn => btn.classList.remove('active'));
            element.classList.add('active');
            
            currentSeverityFilter = severity;
            applyFilters();
        }}

        function applyFilters() {{
            const query = searchInput.value.toLowerCase();
            
            // Filter main table
            const mainRows = document.querySelectorAll('#tableBody .issue-row');
            mainRows.forEach(row => {{
                const severity = row.getAttribute('data-severity');
                const rowText = row.textContent.toLowerCase();

                const matchesSeverity = (currentSeverityFilter === 'All' || severity === currentSeverityFilter);
                const matchesSearch = rowText.includes(query);

                row.style.display = (matchesSeverity && matchesSearch) ? '' : 'none';
            }});

            // Filter info table if present
            const infoRows = document.querySelectorAll('#infoTableBody tr');
            infoRows.forEach(row => {{
                const rowText = row.textContent.toLowerCase();
                row.style.display = rowText.includes(query) ? '' : 'none';
            }});
        }}

        // Sorting Logic for main table
        let sortDirections = [true, true, true, true, true];
        function sortTable(colIndex) {{
            const tbody = document.getElementById(""tableBody"");
            const rows = Array.from(tbody.querySelectorAll("".issue-row""));
            if (rows.length === 0) return;

            const isAscending = sortDirections[colIndex];
            sortDirections[colIndex] = !isAscending;

            rows.sort((a, b) => {{
                const cellA = a.cells[colIndex].textContent.trim();
                const cellB = b.cells[colIndex].textContent.trim();
                
                return isAscending 
                    ? cellA.localeCompare(cellB, undefined, {{ numeric: true, sensitivity: 'base' }})
                    : cellB.localeCompare(cellA, undefined, {{ numeric: true, sensitivity: 'base' }});
            }});

            rows.forEach(row => tbody.appendChild(row));
        }}

        // Sorting Logic for info table
        let infoSortDirections = [true, true, true, true, true];
        function sortInfoTable(colIndex) {{
            const tbody = document.getElementById(""infoTableBody"");
            if (!tbody) return;
            const rows = Array.from(tbody.querySelectorAll(""tr""));
            if (rows.length === 0) return;

            const isAscending = infoSortDirections[colIndex];
            infoSortDirections[colIndex] = !isAscending;

            rows.sort((a, b) => {{
                const cellA = a.cells[colIndex].textContent.trim();
                const cellB = b.cells[colIndex].textContent.trim();
                
                return isAscending 
                    ? cellA.localeCompare(cellB, undefined, {{ numeric: true, sensitivity: 'base' }})
                    : cellB.localeCompare(cellA, undefined, {{ numeric: true, sensitivity: 'base' }});
            }});

            rows.forEach(row => tbody.appendChild(row));
        }}
    </script>
</body>
</html>";
        }
    }
}
