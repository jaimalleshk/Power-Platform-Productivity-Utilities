# Requirements Document: Power Platform Productivity Utilities

This document tracks all collected and active requirements for the **Power Platform Productivity Utilities** suite.

---

## 1. Core Utilities Requirements (Milestone 1)

### 1.1 Shared Core Project (`PowerPlatform.ProductivityEngine.Core`)
- **Authentication**: Acquire OAuth tokens via MSAL (interactive and client secret). Cache tokens in a thread-safe static dictionary to prevent redundant acquisitions.
- **Connection Factory**: Initialize `HttpClient` and SDK `ServiceClient` with default headers (`OData-MaxVersion`, `OData-Version`, `Accept`, and `Prefer: odata.include-annotations="*"`).
- **Resilience Handler**: Intercept HTTP 429 (Too Many Requests), parse `Retry-After` headers, and apply exponential backoffs with cross-thread throttling coordination using a shared `SemaphoreSlim(1,1)`. Retries transient errors (502, 503, 504) up to 5 times.
- **Progress Reporting**: Publish execution stages, status metrics, and percentages via `IProgress<ProgressUpdate>`.

### 1.2 Solution Deep Validator (`Utilities.SolutionDeepValidator`)
- **In-Memory Analysis**: Parse solution ZIP packages (manifests, entity customization XMLs) strictly in-memory.
- **Staging Checks**: Call the async `StageSolution` Web API endpoint, check warnings/errors, and parse the output results.
- **Delta Crawler**: Crawl 11 metadata sources in parallel using resilient paging (page size 5,000) to find missing tables, schema discrepancies, customization locks, and active unmanaged layers.
- **Report Generation**: Output interactive, self-contained HTML dashboards and CI/CD-friendly JSON files.

### 1.3 Solution Repair & Distiller (`Utilities.SolutionRepairDistiller`)
- **Bloat Distillation**: Clean out-of-the-box (OOB) table configurations and re-add explicit subcomponents using `DoNotIncludeSubcomponents = true` to shrink solution file sizes by up to 80%.
- **Local XML Repair**: Parse and repair duplicate namespaces and bad tag syntax in `solution.xml` and `customizations.xml` before import validation.
- **Active Layer Removal**: Programmatically remove active unmanaged layers from target forms and workflows based on validator report diagnostics.

---

## 2. Multi-Environment User & BU Management Requirements (Module 6)

### 2.1 Environment Discovery & Scope
- **Tenant Discovery**: Query the Dataverse Global Discovery Service (`https://globaldisco.crm.dynamics.com/api/discovery/v9.0/Instances`) to automatically locate all active instances/environments in the tenant.
- **Selected or All Scopes**: Allow users to execute reports and assignments targeting either a specific environment unique name, a list of environments, or all discovered environments in the tenant.

### 2.2 User-Centric Reporting & Queries
- **User Role & BU Lookup**: Search for single or multiple users (by email or domain name) across the specified environment scopes.
- **Role Alignment Report**: Consolidate active statuses, Business Unit associations, and assigned security roles for each user in each environment.

### 2.3 Role-Centric & BU-Centric Reporting & Queries (New)
- **Role-to-User Audit**: For one or multiple selected security roles (e.g., "System Administrator", "Environment Maker"), audit and list all users who possess these roles in the targeted environments.
- **BU-to-User Audit**: For one or multiple selected Business Units (e.g., "Main BU", "Sales Division"), audit and list all users who belong to these BUs in the targeted environments.

### 2.4 Role and Business Unit Operations
- **Role Assignment**: Assign one or multiple security roles to target users across targeted environments.
- **Role Removal**: Remove assigned security roles from users across targeted environments.
- **Business Unit Reassignment**: Transfer users to a target Business Unit using the Dataverse `SetBusinessUnit` action, while automatically assigning a valid role from the new BU (as required by the platform).
- **Dry-Run (Simulation)**: Support a `--simulate` flag to print proposed role associations or BU modifications to the console without calling the live APIs.

---

## 3. Reporting and Output Requirements
- **JSON Report File**: Output full, structured telemetry and audit logs of the user roles, BU alignments, role assignments, or audit matrices. The schema must be clean, machine-readable, and suitable for CI/CD pipeline triggers.
- **Interactive HTML Report**: Output a standalone, premium-styled HTML report.
  - **Aesthetic Guidelines**: Must follow the premium aesthetic tokens (navy header, color-coded badges, dark/light toggle options, Outfit/Inter typography, subtle gradients, and glassmorphism cards).
  - **Interactivity**: Include user search filters, status tabs (Active vs. Disabled users), a comparison matrix grid showing environment alignments, and collapsible tables.
  - **No External Dependencies**: Keep the HTML file self-contained for easy distribution and offline usage.

---

## 4. Uniqueness & Comparison Analysis
The following matrix compares the Capabilities of **Power Platform Productivity Utilities (Module 6)** with native Microsoft tools, XrmToolBox, and open-source GitHub scripts.

| Feature | PP Productivity Utilities (Module 6) | MS Power Platform CLI (`pac`) | MS PowerShell Admin Cmdlets | XrmToolBox Plugins | GitHub / Community Scripts |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Execution Mode** | CLI (Headless) & Automation | CLI (Headless) | Scripting | GUI Desktop App | Scripting |
| **Multi-Env Auto-Discovery** | **Yes** (Tenant Global Disco API) | No (Manual per connection) | No (Manual iteration) | No (Manual per connection) | No (Hardcoded environment lists) |
| **Parallel Crawling** | **Yes** (Resilient Task Parallelism) | No (Single connection) | No (Synchronous loops) | No (Active environment only) | No (Slow serial execution) |
| **User Role & BU Matrix** | **Yes** (Cross-environment matrix) | No | No | No (Single environment context) | No |
| **Tenant-Wide Auditing** | **Yes** (Find roles/BUs in all envs) | No | No | No (Compare roles only, no users) | No |
| **Intelligent BU Transfers** | **Yes** (Auto-binds valid BU roles) | No (Requires raw input ID) | No (Requires manual GUIDs) | Yes (User Security Manager) | No (Errors out on missing roles) |
| **Throttling Resilience** | **Yes** (Semaphore-locked HTTP 429) | Yes | Yes (Partial) | No (Crashes on rate-limiting) | No (Fails on API limits) |
| **Formatted Outputs** | **JSON & HTML Dashboards** | Standard Text / Table | Console Objects | Visual Grid / Excel Export | Raw CSV / Console Output |

### Key Business & Operational Values of Our Solution
1. **Instant Compliance & Security Auditing**: Instantly trace a user's access across the entire tenant in a single matrix dashboard. Security teams can verify that offboarded employees or external contractors have had all their roles revoked globally, eliminating security gaps.
2. **Privilege Creep Prevention**: Audit high-privilege roles (like "System Administrator") across all production, sandbox, and development environments simultaneously. This prevents users from retaining administrative access when they transition off a project.
3. **Consolidated Business Unit Alignment**: Easily audit and ensure that users are placed in the correct corporate boundaries (Business Units) across all environments, maintaining strict data privacy compliance.
4. **Zero-Friction Bulk Provisioning**: Onboard teams or update security access across Dev, Test, and UAT environments in a single action, eliminating hours of repetitive click-through tasks.
5. **Risk-Free Executions (Dry Runs)**: Validate exactly which users and environments will be impacted before any changes are committed, preventing accidental user lockouts or configuration errors.
6. **Executive-Ready Sharing**: Automatically generates a professional, interactive HTML dashboard suitable for sharing directly with security officers, business stakeholders, and compliance auditors.
