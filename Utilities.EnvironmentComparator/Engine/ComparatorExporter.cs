using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using Utilities.EnvironmentComparator.Models;
using Utilities.EnvironmentComparator.Storage;

namespace Utilities.EnvironmentComparator.Engine
{
    public class ComparatorExporter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public void ExportToJson(string path, ComparisonResult result)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));

            string json = JsonSerializer.Serialize(result, JsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public void ExportToCsvExcel(string path, ComparisonResult result)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));

            var sb = new StringBuilder();
            var envHeaders = result.TargetEnvironmentNames;
            sb.AppendLine($"RootCategory,SubCategory,ComponentKey,Status,{string.Join(",", envHeaders)}");

            void WriteNodes(IEnumerable<DiffNode> nodes)
            {
                foreach (var node in nodes)
                {
                    var envVals = envHeaders.Select(env => 
                        node.EnvironmentValues.TryGetValue(env, out var v) ? $"\"{v.Replace("\"", "\"\"")}\"" : "\"Missing\"");

                    sb.AppendLine($"\"{node.RootCategory}\",\"{node.SubCategory}\",\"{node.UniqueKey}\",\"{node.Status}\",{string.Join(",", envVals)}");
                }
            }

            WriteNodes(result.AdminSettingsNodes);
            WriteNodes(result.MetadataNodes);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public void ExportFormattedExcel(string path, ComparisonResult result)
        {
            var excelGen = new ExcelReportGenerator();
            excelGen.ExportFormattedExcelXml(path, result);
        }

        public void ExportToHtml(string path, ComparisonResult result)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));

            string html = GenerateHtmlReportContent(result);
            File.WriteAllText(path, html, Encoding.UTF8);
        }

        private string GenerateHtmlReportContent(ComparisonResult result)
        {
            var envs = result.TargetEnvironmentNames;
            var envHeadersHtml = string.Join("", envs.Select(e => $"<th>{e}</th>"));

            StringBuilder adminRowsHtml = new();
            foreach (var node in result.AdminSettingsNodes)
            {
                string badgeClass = node.Status switch
                {
                    DiffStatus.Identical => "badge-identical",
                    DiffStatus.Delta => "badge-delta",
                    _ => "badge-unique"
                };

                string envCellsHtml = string.Join("", envs.Select(e => {
                    string val = node.EnvironmentValues.TryGetValue(e, out var v) ? v : "Missing";
                    return $"<td>{val}</td>";
                }));

                adminRowsHtml.AppendLine($@"
                    <tr>
                        <td><span class=""sub-cat"">{node.SubCategory}</span></td>
                        <td><strong>{node.DisplayName}</strong></td>
                        <td><span class=""badge {badgeClass}"">{node.Status}</span></td>
                        {envCellsHtml}
                    </tr>");
            }

            StringBuilder metadataRowsHtml = new();
            foreach (var node in result.MetadataNodes)
            {
                string badgeClass = node.Status switch
                {
                    DiffStatus.Identical => "badge-identical",
                    DiffStatus.Delta => "badge-delta",
                    _ => "badge-unique"
                };

                string envCellsHtml = string.Join("", envs.Select(e => {
                    string val = node.EnvironmentValues.TryGetValue(e, out var v) ? v : "Missing";
                    return $"<td>{val}</td>";
                }));

                metadataRowsHtml.AppendLine($@"
                    <tr>
                        <td><span class=""sub-cat"">{node.SubCategory}</span></td>
                        <td><strong>{node.DisplayName}</strong></td>
                        <td><span class=""badge {badgeClass}"">{node.Status}</span></td>
                        {envCellsHtml}
                    </tr>");
            }

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>Power Platform Environment Comparison Dashboard</title>
    <style>
        :root {{
            --bg-color: #0f172a;
            --card-bg: rgba(30, 41, 59, 0.7);
            --border-color: #334155;
            --text-main: #f8fafc;
            --text-muted: #94a3b8;
            --accent-blue: #38bdf8;
            --accent-green: #22c55e;
            --accent-warning: #f59e0b;
        }}
        body {{
            font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
            background-color: var(--bg-color);
            color: var(--text-main);
            margin: 0;
            padding: 24px;
        }}
        .header {{
            background: linear-gradient(135deg, #1e293b, #0f172a);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 24px;
            margin-bottom: 24px;
            box-shadow: 0 4px 20px rgba(0,0,0,0.3);
        }}
        .header h1 {{ margin: 0 0 8px 0; color: var(--accent-blue); font-size: 24px; }}
        .stats-bar {{
            display: flex;
            gap: 16px;
            margin-top: 16px;
        }}
        .stat-card {{
            background: var(--card-bg);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 12px 20px;
            flex: 1;
        }}
        .stat-value {{ font-size: 22px; font-weight: bold; margin-top: 4px; }}
        .section-card {{
            background: var(--card-bg);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 20px;
            margin-bottom: 24px;
        }}
        .section-title {{ font-size: 18px; font-weight: 600; margin-bottom: 16px; color: var(--accent-blue); }}
        table {{
            width: 100%;
            border-collapse: collapse;
            font-size: 14px;
        }}
        th, td {{
            text-align: left;
            padding: 10px 14px;
            border-bottom: 1px solid var(--border-color);
        }}
        th {{ background-color: rgba(51, 65, 85, 0.5); color: var(--text-muted); }}
        .badge {{
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 12px;
            font-weight: 600;
            display: inline-block;
        }}
        .badge-identical {{ background: rgba(34, 197, 94, 0.2); color: #4ade80; border: 1px solid #22c55e; }}
        .badge-delta {{ background: rgba(245, 158, 11, 0.2); color: #fbbf24; border: 1px solid #f59e0b; }}
        .badge-unique {{ background: rgba(56, 189, 248, 0.2); color: #38bdf8; border: 1px solid #38bdf8; }}
        .sub-cat {{ color: var(--text-muted); font-size: 12px; background: #334155; padding: 2px 6px; border-radius: 4px; }}
    </style>
</head>
<body>
    <div class=""header"">
        <h1>⚡ Power Platform N-Way Environment Comparison Dashboard</h1>
        <p style=""color: var(--text-muted); margin: 0;"">Compared Environments: {string.Join(", ", envs)} | Generated: {result.ComparedAt:yyyy-MM-dd HH:mm:ss} UTC</p>
        <div class=""stats-bar"">
            <div class=""stat-card""><div>Total Items</div><div class=""stat-value"">{result.TotalCount}</div></div>
            <div class=""stat-card""><div>Identical 🟢</div><div class=""stat-value"" style=""color:#4ade80"">{result.IdenticalCount}</div></div>
            <div class=""stat-card""><div>Deltas ⚠️</div><div class=""stat-value"" style=""color:#fbbf24"">{result.DeltaCount}</div></div>
            <div class=""stat-card""><div>Unique 🔷</div><div class=""stat-value"" style=""color:#38bdf8"">{result.UniqueCount}</div></div>
        </div>
    </div>

    <div class=""section-card"">
        <div class=""section-title"">📁 Root 1: Admin &amp; Environment Settings</div>
        <table>
            <thead>
                <tr>
                    <th>Category</th>
                    <th>Setting Name</th>
                    <th>Status</th>
                    {envHeadersHtml}
                </tr>
            </thead>
            <tbody>
                {adminRowsHtml}
            </tbody>
        </table>
    </div>

    <div class=""section-card"">
        <div class=""section-title"">📁 Root 2: Metadata &amp; Customizations</div>
        <table>
            <thead>
                <tr>
                    <th>Category</th>
                    <th>Component Name</th>
                    <th>Status</th>
                    {envHeadersHtml}
                </tr>
            </thead>
            <tbody>
                {metadataRowsHtml}
            </tbody>
        </table>
    </div>
</body>
</html>";
        }
    }
}
