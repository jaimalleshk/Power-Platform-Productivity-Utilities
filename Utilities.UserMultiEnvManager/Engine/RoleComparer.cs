using System;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Utilities.UserMultiEnvManager.Models;

namespace Utilities.UserMultiEnvManager.Engine
{
    public class RoleComparer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null // Preserve PascalCase/properties as-is
        };

        public void ExportReportToJson(string path, object report)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (report == null) throw new ArgumentNullException(nameof(report));

            string json = JsonSerializer.Serialize(report, JsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public void ExportUserRoleReportToHtml(string path, UserRoleReport report)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (report == null) throw new ArgumentNullException(nameof(report));

            string html = BuildUserRoleHtml(report);
            File.WriteAllText(path, html, Encoding.UTF8);
        }

        public void ExportRoleAuditReportToHtml(string path, RoleAuditReport report)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (report == null) throw new ArgumentNullException(nameof(report));

            string html = BuildRoleAuditHtml(report);
            File.WriteAllText(path, html, Encoding.UTF8);
        }

        public void ExportBuAuditReportToHtml(string path, BuAuditReport report)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (report == null) throw new ArgumentNullException(nameof(report));

            string html = BuildBuAuditHtml(report);
            File.WriteAllText(path, html, Encoding.UTF8);
        }

        private string BuildUserRoleHtml(UserRoleReport report)
        {
            // Build environments lists and rows for the interactive JavaScript matrix
            string envsJson = JsonSerializer.Serialize(report.EnvironmentsScanned);
            string statusesJson = JsonSerializer.Serialize(report.UserStatuses);

            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Tenant User Roles & BU Alignment Report</title>
    <link href=""https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=Outfit:wght@400;500;600;700;800&display=swap"" rel=""stylesheet"">
    <style>
        :root {{
            --primary: #0F172A;
            --primary-light: #1E293B;
            --accent: #3B82F6;
            --accent-gradient: linear-gradient(135deg, #3B82F6 0%, #1D4ED8 100%);
            --background: #F8FAFC;
            --card-bg: #FFFFFF;
            --text-main: #0F172A;
            --text-muted: #64748B;
            --border: #E2E8F0;
            --success: #10B981;
            --warning: #F59E0B;
            --error: #EF4444;
            --shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1);
        }}

        [data-theme=""dark""] {{
            --primary: #F8FAFC;
            --primary-light: #F1F5F9;
            --accent: #60A5FA;
            --accent-gradient: linear-gradient(135deg, #60A5FA 0%, #3B82F6 100%);
            --background: #0F172A;
            --card-bg: #1E293B;
            --text-main: #F1F5F9;
            --text-muted: #94A3B8;
            --border: #334155;
            --success: #34D399;
            --warning: #FBBF24;
            --error: #F87171;
            --shadow: 0 4px 6px -1px rgb(0 0 0 / 0.3), 0 2px 4px -2px rgb(0 0 0 / 0.3);
        }}

        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}

        body {{
            font-family: 'Inter', sans-serif;
            background-color: var(--background);
            color: var(--text-main);
            padding: 2rem;
            transition: background-color 0.3s, color 0.3s;
        }}

        header {{
            background: linear-gradient(135deg, #0F172A 0%, #1E293B 100%);
            color: #FFFFFF;
            border-radius: 16px;
            padding: 2.5rem;
            margin-bottom: 2rem;
            box-shadow: var(--shadow);
            position: relative;
            overflow: hidden;
        }}

        header::after {{
            content: '';
            position: absolute;
            top: -50%;
            right: -20%;
            width: 300px;
            height: 300px;
            background: radial-gradient(circle, rgba(59, 130, 246, 0.2) 0%, transparent 70%);
            pointer-events: none;
        }}

        h1 {{
            font-family: 'Outfit', sans-serif;
            font-size: 2rem;
            font-weight: 700;
            margin-bottom: 0.5rem;
            letter-spacing: -0.025em;
        }}

        .meta-text {{
            font-size: 0.875rem;
            color: #94A3B8;
        }}

        .theme-toggle {{
            position: absolute;
            top: 2rem;
            right: 2rem;
            background: rgba(255, 255, 255, 0.1);
            border: 1px solid rgba(255, 255, 255, 0.2);
            color: white;
            padding: 0.5rem 1rem;
            border-radius: 9999px;
            cursor: pointer;
            font-size: 0.875rem;
            font-weight: 500;
            transition: background 0.2s;
        }}

        .theme-toggle:hover {{
            background: rgba(255, 255, 255, 0.2);
        }}

        .stats-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 1.5rem;
            margin-bottom: 2rem;
        }}

        .stat-card {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 1.5rem;
            box-shadow: var(--shadow);
            display: flex;
            flex-direction: column;
            justify-content: space-between;
            transition: transform 0.2s, border-color 0.2s;
        }}

        .stat-card:hover {{
            transform: translateY(-2px);
            border-color: var(--accent);
        }}

        .stat-card .label {{
            font-size: 0.875rem;
            color: var(--text-muted);
            font-weight: 500;
            margin-bottom: 0.5rem;
        }}

        .stat-card .value {{
            font-size: 1.75rem;
            font-weight: 700;
            font-family: 'Outfit', sans-serif;
            color: var(--text-main);
        }}

        .filters-card {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 1.5rem;
            box-shadow: var(--shadow);
            margin-bottom: 2rem;
        }}

        .filters-title {{
            font-family: 'Outfit', sans-serif;
            font-size: 1.1rem;
            font-weight: 600;
            margin-bottom: 1rem;
        }}

        .filters-flex {{
            display: flex;
            flex-wrap: wrap;
            gap: 1.5rem;
            align-items: center;
        }}

        .search-box {{
            flex: 1;
            min-width: 250px;
        }}

        .search-input {{
            width: 100%;
            padding: 0.75rem 1rem;
            border-radius: 8px;
            border: 1px solid var(--border);
            background-color: var(--card-bg);
            color: var(--text-main);
            font-size: 0.875rem;
            outline: none;
            transition: border-color 0.2s;
        }}

        .search-input:focus {{
            border-color: var(--accent);
        }}

        .filter-group {{
            display: flex;
            align-items: center;
            gap: 0.75rem;
        }}

        .filter-label {{
            font-size: 0.875rem;
            font-weight: 500;
            color: var(--text-muted);
        }}

        .checkbox-container {{
            display: flex;
            align-items: center;
            gap: 0.5rem;
            cursor: pointer;
            font-size: 0.875rem;
            color: var(--text-main);
        }}

        .checkbox-container input {{
            cursor: pointer;
        }}

        .report-section {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            box-shadow: var(--shadow);
            padding: 1.5rem;
            overflow-x: auto;
        }}

        .section-title {{
            font-family: 'Outfit', sans-serif;
            font-size: 1.25rem;
            font-weight: 600;
            margin-bottom: 1.25rem;
            display: flex;
            align-items: center;
            justify-content: space-between;
        }}

        table {{
            width: 100%;
            border-collapse: collapse;
            text-align: left;
            font-size: 0.875rem;
        }}

        th {{
            background-color: var(--background);
            color: var(--text-muted);
            font-weight: 600;
            padding: 1rem;
            border-bottom: 2px solid var(--border);
            font-size: 0.75rem;
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }}

        td {{
            padding: 1rem;
            border-bottom: 1px solid var(--border);
            vertical-align: top;
        }}

        tr:hover td {{
            background-color: rgba(59, 130, 246, 0.03);
        }}

        .badge {{
            display: inline-block;
            padding: 0.25rem 0.5rem;
            border-radius: 9999px;
            font-size: 0.75rem;
            font-weight: 600;
            text-align: center;
        }}

        .badge-active {{
            background-color: rgba(16, 185, 129, 0.15);
            color: var(--success);
        }}

        .badge-disabled {{
            background-color: rgba(239, 68, 68, 0.15);
            color: var(--error);
        }}

        .badge-missing {{
            background-color: rgba(100, 116, 139, 0.1);
            color: var(--text-muted);
            font-weight: 400;
        }}

        .badge-role {{
            background-color: rgba(59, 130, 246, 0.1);
            color: var(--accent);
            margin: 0.125rem;
            border: 1px solid rgba(59, 130, 246, 0.2);
            border-radius: 4px;
            padding: 0.125rem 0.375rem;
            font-size: 0.75rem;
            display: inline-block;
        }}

        .bu-name {{
            font-weight: 500;
            color: var(--text-main);
            margin-bottom: 0.25rem;
        }}

        .matrix-cell {{
            max-width: 250px;
        }}
    </style>
</head>
<body>
    <header>
        <button class=""theme-toggle"" onclick=""toggleTheme()"">Toggle Theme</button>
        <h1>Tenant User Roles & BU Alignment</h1>
        <div class=""meta-text"">Generated on: {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Multi-Environment Identity Audit</div>
    </header>

    <div class=""stats-grid"">
        <div class=""stat-card"">
            <span class=""label"">Scanned Users</span>
            <span class=""value"">{report.ScannedEmails.Count}</span>
        </div>
        <div class=""stat-card"">
            <span class=""label"">Scanned Environments</span>
            <span class=""value"">{report.EnvironmentsScanned.Count}</span>
        </div>
        <div class=""stat-card"">
            <span class=""label"">Active Environments</span>
            <span class=""value"" id=""active-envs-count"">-</span>
        </div>
    </div>

    <div class=""filters-card"">
        <div class=""filters-title"">Filter Alignment Matrix</div>
        <div class=""filters-flex"">
            <div class=""search-box"">
                <input type=""text"" id=""search-input"" class=""search-input"" placeholder=""Search by user email..."" oninput=""filterMatrix()"">
            </div>
            <div class=""filter-group"">
                <span class=""filter-label"">Filters:</span>
                <label class=""checkbox-container"">
                    <input type=""checkbox"" id=""filter-exists"" checked onchange=""filterMatrix()""> User exists in Env
                </label>
                <label class=""checkbox-container"">
                    <input type=""checkbox"" id=""filter-missing"" checked onchange=""filterMatrix()""> User not present
                </label>
            </div>
        </div>
    </div>

    <div class=""report-section"">
        <div class=""section-title"">
            <span>Identity Comparison Grid</span>
        </div>
        <table id=""matrix-table"">
            <thead>
                <tr>
                    <th>User Email</th>
                    {string.Join("\n", report.EnvironmentsScanned.Select(e => $"<th>{e}</th>"))}
                </tr>
            </thead>
            <tbody id=""matrix-body"">
                <!-- Injected via JavaScript -->
            </tbody>
        </table>
    </div>

    <script>
        const environments = {envsJson};
        const userStatuses = {statusesJson};

        function toggleTheme() {{
            const currentTheme = document.body.getAttribute('data-theme');
            if (currentTheme === 'dark') {{
                document.body.removeAttribute('data-theme');
            }} else {{
                document.body.setAttribute('data-theme', 'dark');
            }}
        }}

        function buildMatrix() {{
            const tbody = document.getElementById('matrix-body');
            tbody.innerHTML = '';

            const searchQuery = document.getElementById('search-input').value.toLowerCase();
            const showExists = document.getElementById('filter-exists').checked;
            const showMissing = document.getElementById('filter-missing').checked;

            let totalActiveMappings = 0;

            for (const [email, envList] of Object.entries(userStatuses)) {{
                if (searchQuery && !email.includes(searchQuery)) {{
                    continue;
                }}

                // Determine if user has any matches
                const hasExists = envList.some(e => e.UserExists);
                const hasMissing = envList.some(e => !e.UserExists);

                if (!showExists && hasExists && !hasMissing) continue;
                if (!showMissing && !hasExists) continue;

                const tr = document.createElement('tr');
                
                const tdEmail = document.createElement('td');
                tdEmail.style.fontWeight = '600';
                tdEmail.textContent = email;
                tr.appendChild(tdEmail);

                environments.forEach(env => {{
                    const td = document.createElement('td');
                    td.className = 'matrix-cell';
                    
                    const envStatus = envList.find(e => e.EnvironmentUniqueName === env);
                    if (envStatus && envStatus.UserExists) {{
                        totalActiveMappings++;
                        const statusBadge = document.createElement('span');
                        statusBadge.className = 'badge ' + (envStatus.IsDisabled ? 'badge-disabled' : 'badge-active');
                        statusBadge.textContent = envStatus.IsDisabled ? 'Disabled' : 'Active';
                        td.appendChild(statusBadge);

                        if (envStatus.BusinessUnitName) {{
                            const buDiv = document.createElement('div');
                            buDiv.className = 'bu-name';
                            buDiv.style.marginTop = '0.5rem';
                            buDiv.textContent = 'BU: ' + envStatus.BusinessUnitName;
                            td.appendChild(buDiv);
                        }}

                        if (envStatus.Roles && envStatus.Roles.length > 0) {{
                            const rolesDiv = document.createElement('div');
                            rolesDiv.style.marginTop = '0.25rem';
                            envStatus.Roles.forEach(r => {{
                                const rSpan = document.createElement('span');
                                rSpan.className = 'badge-role';
                                rSpan.textContent = r;
                                rolesDiv.appendChild(rSpan);
                            }});
                            td.appendChild(rolesDiv);
                        }}
                    }} else {{
                        const missingSpan = document.createElement('span');
                        missingSpan.className = 'badge badge-missing';
                        missingSpan.textContent = envStatus && envStatus.BusinessUnitName.startsWith('SCAN_ERROR') 
                            ? 'Error' 
                            : 'Not Present';
                        td.appendChild(missingSpan);
                        
                        if (envStatus && envStatus.BusinessUnitName.startsWith('SCAN_ERROR')) {{
                            const errDiv = document.createElement('div');
                            errDiv.className = 'bu-name';
                            errDiv.style.color = 'var(--error)';
                            errDiv.style.fontSize = '0.75rem';
                            errDiv.style.marginTop = '0.5rem';
                            errDiv.textContent = envStatus.BusinessUnitName;
                            td.appendChild(errDiv);
                        }}
                    }}
                    tr.appendChild(td);
                }});

                tbody.appendChild(tr);
            }}

            document.getElementById('active-envs-count').textContent = totalActiveMappings;
        }}

        function filterMatrix() {{
            buildMatrix();
        }}

        // Initialize table
        buildMatrix();
    </script>
</body>
</html>");

            return sb.ToString();
        }

        private string BuildRoleAuditHtml(RoleAuditReport report)
        {
            string matchesJson = JsonSerializer.Serialize(report.Matches);
            string targetsJson = JsonSerializer.Serialize(report.TargetRoles);

            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Security Role Compliance Audit Report</title>
    <link href=""https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=Outfit:wght@400;500;600;700;800&display=swap"" rel=""stylesheet"">
    <style>
        :root {{
            --primary: #0F172A;
            --primary-light: #1E293B;
            --accent: #3B82F6;
            --accent-gradient: linear-gradient(135deg, #3B82F6 0%, #1D4ED8 100%);
            --background: #F8FAFC;
            --card-bg: #FFFFFF;
            --text-main: #0F172A;
            --text-muted: #64748B;
            --border: #E2E8F0;
            --success: #10B981;
            --warning: #F59E0B;
            --error: #EF4444;
            --shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1);
        }}

        [data-theme=""dark""] {{
            --primary: #F8FAFC;
            --primary-light: #F1F5F9;
            --accent: #60A5FA;
            --accent-gradient: linear-gradient(135deg, #60A5FA 0%, #3B82F6 100%);
            --background: #0F172A;
            --card-bg: #1E293B;
            --text-main: #F1F5F9;
            --text-muted: #94A3B8;
            --border: #334155;
            --success: #34D399;
            --warning: #FBBF24;
            --error: #F87171;
            --shadow: 0 4px 6px -1px rgb(0 0 0 / 0.3), 0 2px 4px -2px rgb(0 0 0 / 0.3);
        }}

        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}

        body {{
            font-family: 'Inter', sans-serif;
            background-color: var(--background);
            color: var(--text-main);
            padding: 2rem;
            transition: background-color 0.3s, color 0.3s;
        }}

        header {{
            background: linear-gradient(135deg, #0F172A 0%, #1E293B 100%);
            color: #FFFFFF;
            border-radius: 16px;
            padding: 2.5rem;
            margin-bottom: 2rem;
            box-shadow: var(--shadow);
            position: relative;
            overflow: hidden;
        }}

        h1 {{
            font-family: 'Outfit', sans-serif;
            font-size: 2rem;
            font-weight: 700;
            margin-bottom: 0.5rem;
        }}

        .meta-text {{
            font-size: 0.875rem;
            color: #94A3B8;
        }}

        .theme-toggle {{
            position: absolute;
            top: 2rem;
            right: 2rem;
            background: rgba(255, 255, 255, 0.1);
            border: 1px solid rgba(255, 255, 255, 0.2);
            color: white;
            padding: 0.5rem 1rem;
            border-radius: 9999px;
            cursor: pointer;
            font-size: 0.875rem;
            transition: background 0.2s;
        }}

        .theme-toggle:hover {{
            background: rgba(255, 255, 255, 0.2);
        }}

        .stats-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 1.5rem;
            margin-bottom: 2rem;
        }}

        .stat-card {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 1.5rem;
            box-shadow: var(--shadow);
        }}

        .stat-card .label {{
            font-size: 0.875rem;
            color: var(--text-muted);
            margin-bottom: 0.5rem;
        }}

        .stat-card .value {{
            font-size: 1.75rem;
            font-weight: 700;
            font-family: 'Outfit', sans-serif;
        }}

        .filters-card {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 1.5rem;
            box-shadow: var(--shadow);
            margin-bottom: 2rem;
        }}

        .filters-flex {{
            display: flex;
            flex-wrap: wrap;
            gap: 1.5rem;
            align-items: center;
        }}

        .search-input {{
            flex: 1;
            min-width: 250px;
            padding: 0.75rem 1rem;
            border-radius: 8px;
            border: 1px solid var(--border);
            background-color: var(--card-bg);
            color: var(--text-main);
        }}

        .report-section {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            box-shadow: var(--shadow);
            padding: 1.5rem;
        }}

        table {{
            width: 100%;
            border-collapse: collapse;
        }}

        th {{
            background-color: var(--background);
            color: var(--text-muted);
            padding: 1rem;
            border-bottom: 2px solid var(--border);
            font-size: 0.75rem;
            text-transform: uppercase;
        }}

        td {{
            padding: 1rem;
            border-bottom: 1px solid var(--border);
        }}

        .badge {{
            display: inline-block;
            padding: 0.25rem 0.5rem;
            border-radius: 9999px;
            font-size: 0.75rem;
            font-weight: 600;
        }}

        .badge-active {{
            background-color: rgba(16, 185, 129, 0.15);
            color: var(--success);
        }}

        .badge-disabled {{
            background-color: rgba(239, 68, 68, 0.15);
            color: var(--error);
        }}
    </style>
</head>
<body>
    <header>
        <button class=""theme-toggle"" onclick=""toggleTheme()"">Toggle Theme</button>
        <h1>Security Role Compliance Audit Report</h1>
        <div class=""meta-text"">Generated on: {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</div>
    </header>

    <div class=""stats-grid"">
        <div class=""stat-card"">
            <span class=""label"">Audited Roles</span>
            <span class=""value"">{report.TargetRoles.Count}</span>
        </div>
        <div class=""stat-card"">
            <span class=""label"">Scanned Environments</span>
            <span class=""value"">{report.EnvironmentsScanned.Count}</span>
        </div>
        <div class=""stat-card"">
            <span class=""label"">Total Access Matches</span>
            <span class=""value"" id=""total-matches"">{report.Matches.Count}</span>
        </div>
    </div>

    <div class=""filters-card"">
        <div class=""filters-flex"">
            <input type=""text"" id=""search-input"" class=""search-input"" placeholder=""Filter by user, role, or environment..."" oninput=""filterAudit()"">
        </div>
    </div>

    <div class=""report-section"">
        <table>
            <thead>
                <tr>
                    <th>Environment</th>
                    <th>Audited Role</th>
                    <th>User Name</th>
                    <th>User Email</th>
                    <th>Status</th>
                </tr>
            </thead>
            <tbody id=""audit-body"">
                <!-- Injected via JavaScript -->
            </tbody>
        </table>
    </div>

    <script>
        const matches = {matchesJson};

        function toggleTheme() {{
            const currentTheme = document.body.getAttribute('data-theme');
            if (currentTheme === 'dark') {{
                document.body.removeAttribute('data-theme');
            }} else {{
                document.body.setAttribute('data-theme', 'dark');
            }}
        }}

        function buildAudit() {{
            const tbody = document.getElementById('audit-body');
            tbody.innerHTML = '';

            const q = document.getElementById('search-input').value.toLowerCase();
            let count = 0;

            matches.forEach(m => {{
                if (q && !m.EnvironmentUniqueName.toLowerCase().includes(q) && 
                    !m.TargetName.toLowerCase().includes(q) && 
                    !m.UserName.toLowerCase().includes(q) && 
                    !m.UserEmail.toLowerCase().includes(q)) {{
                    return;
                }}

                count++;
                const tr = document.createElement('tr');
                
                const tdEnv = document.createElement('td');
                tdEnv.textContent = m.EnvironmentUniqueName;
                tdEnv.style.fontWeight = '600';
                tr.appendChild(tdEnv);

                const tdRole = document.createElement('td');
                const roleBadge = document.createElement('span');
                roleBadge.className = 'badge';
                roleBadge.style.backgroundColor = 'rgba(59, 130, 246, 0.1)';
                roleBadge.style.color = 'var(--accent)';
                roleBadge.textContent = m.TargetName;
                tdRole.appendChild(roleBadge);
                tr.appendChild(tdRole);

                const tdName = document.createElement('td');
                tdName.textContent = m.UserName;
                tr.appendChild(tdName);

                const tdEmail = document.createElement('td');
                tdEmail.textContent = m.UserEmail;
                tr.appendChild(tdEmail);

                const tdStatus = document.createElement('td');
                const statusBadge = document.createElement('span');
                statusBadge.className = 'badge ' + (m.IsDisabled ? 'badge-disabled' : 'badge-active');
                statusBadge.textContent = m.IsDisabled ? 'Disabled' : 'Active';
                tdStatus.appendChild(statusBadge);
                tr.appendChild(tdStatus);

                tbody.appendChild(tr);
            }});

            document.getElementById('total-matches').textContent = count;
        }}

        buildAudit();
    </script>
</body>
</html>");
            return sb.ToString();
        }

        private string BuildBuAuditHtml(BuAuditReport report)
        {
            string matchesJson = JsonSerializer.Serialize(report.Matches);

            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Business Unit Membership Compliance Report</title>
    <link href=""https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=Outfit:wght@400;500;600;700;800&display=swap"" rel=""stylesheet"">
    <style>
        :root {{
            --primary: #0F172A;
            --primary-light: #1E293B;
            --accent: #3B82F6;
            --accent-gradient: linear-gradient(135deg, #3B82F6 0%, #1D4ED8 100%);
            --background: #F8FAFC;
            --card-bg: #FFFFFF;
            --text-main: #0F172A;
            --text-muted: #64748B;
            --border: #E2E8F0;
            --success: #10B981;
            --warning: #F59E0B;
            --error: #EF4444;
            --shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1);
        }}

        [data-theme=""dark""] {{
            --primary: #F8FAFC;
            --primary-light: #F1F5F9;
            --accent: #60A5FA;
            --accent-gradient: linear-gradient(135deg, #60A5FA 0%, #3B82F6 100%);
            --background: #0F172A;
            --card-bg: #1E293B;
            --text-main: #F1F5F9;
            --text-muted: #94A3B8;
            --border: #334155;
            --success: #34D399;
            --warning: #FBBF24;
            --error: #F87171;
            --shadow: 0 4px 6px -1px rgb(0 0 0 / 0.3), 0 2px 4px -2px rgb(0 0 0 / 0.3);
        }}

        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}

        body {{
            font-family: 'Inter', sans-serif;
            background-color: var(--background);
            color: var(--text-main);
            padding: 2rem;
            transition: background-color 0.3s, color 0.3s;
        }}

        header {{
            background: linear-gradient(135deg, #0F172A 0%, #1E293B 100%);
            color: #FFFFFF;
            border-radius: 16px;
            padding: 2.5rem;
            margin-bottom: 2rem;
            box-shadow: var(--shadow);
            position: relative;
            overflow: hidden;
        }}

        h1 {{
            font-family: 'Outfit', sans-serif;
            font-size: 2rem;
            font-weight: 700;
            margin-bottom: 0.5rem;
        }}

        .meta-text {{
            font-size: 0.875rem;
            color: #94A3B8;
        }}

        .theme-toggle {{
            position: absolute;
            top: 2rem;
            right: 2rem;
            background: rgba(255, 255, 255, 0.1);
            border: 1px solid rgba(255, 255, 255, 0.2);
            color: white;
            padding: 0.5rem 1rem;
            border-radius: 9999px;
            cursor: pointer;
            font-size: 0.875rem;
            transition: background 0.2s;
        }}

        .theme-toggle:hover {{
            background: rgba(255, 255, 255, 0.2);
        }}

        .stats-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 1.5rem;
            margin-bottom: 2rem;
        }}

        .stat-card {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 1.5rem;
            box-shadow: var(--shadow);
        }}

        .stat-card .label {{
            font-size: 0.875rem;
            color: var(--text-muted);
            margin-bottom: 0.5rem;
        }}

        .stat-card .value {{
            font-size: 1.75rem;
            font-weight: 700;
            font-family: 'Outfit', sans-serif;
        }}

        .filters-card {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 1.5rem;
            box-shadow: var(--shadow);
            margin-bottom: 2rem;
        }}

        .filters-flex {{
            display: flex;
            flex-wrap: wrap;
            gap: 1.5rem;
            align-items: center;
        }}

        .search-input {{
            flex: 1;
            min-width: 250px;
            padding: 0.75rem 1rem;
            border-radius: 8px;
            border: 1px solid var(--border);
            background-color: var(--card-bg);
            color: var(--text-main);
        }}

        .report-section {{
            background-color: var(--card-bg);
            border: 1px solid var(--border);
            border-radius: 12px;
            box-shadow: var(--shadow);
            padding: 1.5rem;
        }}

        table {{
            width: 100%;
            border-collapse: collapse;
        }}

        th {{
            background-color: var(--background);
            color: var(--text-muted);
            padding: 1rem;
            border-bottom: 2px solid var(--border);
            font-size: 0.75rem;
            text-transform: uppercase;
        }}

        td {{
            padding: 1rem;
            border-bottom: 1px solid var(--border);
        }}

        .badge {{
            display: inline-block;
            padding: 0.25rem 0.5rem;
            border-radius: 9999px;
            font-size: 0.75rem;
            font-weight: 600;
        }}

        .badge-active {{
            background-color: rgba(16, 185, 129, 0.15);
            color: var(--success);
        }}

        .badge-disabled {{
            background-color: rgba(239, 68, 68, 0.15);
            color: var(--error);
        }}
    </style>
</head>
<body>
    <header>
        <button class=""theme-toggle"" onclick=""toggleTheme()"">Toggle Theme</button>
        <h1>Business Unit Membership Compliance Report</h1>
        <div class=""meta-text"">Generated on: {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</div>
    </header>

    <div class=""stats-grid"">
        <div class=""stat-card"">
            <span class=""label"">Audited BUs</span>
            <span class=""value"">{report.TargetBus.Count}</span>
        </div>
        <div class=""stat-card"">
            <span class=""label"">Scanned Environments</span>
            <span class=""value"">{report.EnvironmentsScanned.Count}</span>
        </div>
        <div class=""stat-card"">
            <span class=""label"">Total User Matches</span>
            <span class=""value"" id=""total-matches"">{report.Matches.Count}</span>
        </div>
    </div>

    <div class=""filters-card"">
        <div class=""filters-flex"">
            <input type=""text"" id=""search-input"" class=""search-input"" placeholder=""Filter by user, BU, or environment..."" oninput=""filterAudit()"">
        </div>
    </div>

    <div class=""report-section"">
        <table>
            <thead>
                <tr>
                    <th>Environment</th>
                    <th>Business Unit</th>
                    <th>User Name</th>
                    <th>User Email</th>
                    <th>Status</th>
                </tr>
            </thead>
            <tbody id=""audit-body"">
                <!-- Injected via JavaScript -->
            </tbody>
        </table>
    </div>

    <script>
        const matches = {matchesJson};

        function toggleTheme() {{
            const currentTheme = document.body.getAttribute('data-theme');
            if (currentTheme === 'dark') {{
                document.body.removeAttribute('data-theme');
            }} else {{
                document.body.setAttribute('data-theme', 'dark');
            }}
        }}

        function buildAudit() {{
            const tbody = document.getElementById('audit-body');
            tbody.innerHTML = '';

            const q = document.getElementById('search-input').value.toLowerCase();
            let count = 0;

            matches.forEach(m => {{
                if (q && !m.EnvironmentUniqueName.toLowerCase().includes(q) && 
                    !m.TargetName.toLowerCase().includes(q) && 
                    !m.UserName.toLowerCase().includes(q) && 
                    !m.UserEmail.toLowerCase().includes(q)) {{
                    return;
                }}

                count++;
                const tr = document.createElement('tr');
                
                const tdEnv = document.createElement('td');
                tdEnv.textContent = m.EnvironmentUniqueName;
                tdEnv.style.fontWeight = '600';
                tr.appendChild(tdEnv);

                const tdBu = document.createElement('td');
                const buBadge = document.createElement('span');
                buBadge.className = 'badge';
                buBadge.style.backgroundColor = 'rgba(100, 116, 139, 0.1)';
                buBadge.style.color = 'var(--text-muted)';
                buBadge.textContent = m.TargetName;
                tdBu.appendChild(buBadge);
                tr.appendChild(tdBu);

                const tdName = document.createElement('td');
                tdName.textContent = m.UserName;
                tr.appendChild(tdName);

                const tdEmail = document.createElement('td');
                tdEmail.textContent = m.UserEmail;
                tr.appendChild(tdEmail);

                const tdStatus = document.createElement('td');
                const statusBadge = document.createElement('span');
                statusBadge.className = 'badge ' + (m.IsDisabled ? 'badge-disabled' : 'badge-active');
                statusBadge.textContent = m.IsDisabled ? 'Disabled' : 'Active';
                tdStatus.appendChild(statusBadge);
                tr.appendChild(tdStatus);

                tbody.appendChild(tr);
            }});

            document.getElementById('total-matches').textContent = count;
        }}

        buildAudit();
    </script>
</body>
</html>");
            return sb.ToString();
        }
    }
}
