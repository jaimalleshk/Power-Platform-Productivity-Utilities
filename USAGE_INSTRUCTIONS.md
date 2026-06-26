# Usage Instructions: Power Platform Productivity Engine

This document provides detailed usage instructions for running the **Power Platform Productivity Engine** CLI.

---

## Global Options

The console CLI supports the following subcommands:
*   `validate` - Runs deep checks on local solution zip files or downloaded solutions.
*   `distill` - Optimizes OOB tables on the source environment.
*   `repair` - Automatically resolves active layers on target and missing dependencies on source.

---

## 1. Solution Validation (`validate`)

Performs a static analysis of a solution package against a target Dataverse environment.

### Syntax
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- validate [Options]
```

### Parameters
*   `--zip <path>`: Path to a local solution `.zip` package.
*   `--url <url>`: Target environment Dataverse Web API URL (e.g. `https://myorg.crm.dynamics.com`).
*   `--connstr <connection-string>`: Explicit connection string for target.
*   `--interactive`: Force interactive OAuth authentication.
*   `--simulate`: Run in simulation mode (offline).
*   `--out-json <path>`: Destination path for the JSON report (defaults to `validation_report.json`).
*   `--out-html <path>`: Destination path for the interactive HTML dashboard (defaults to `validation_report.html`).
*   `--src-url <url>`: Source environment URL (used if downloading solution from source).
*   `--src-connstr <connection-string>`: Explicit connection string for source.
*   `--solution <name>`: Solution unique name to download from source.
*   `--validation-log <path>`: Path to an import log `.xml` or `.zip` containing validation errors (e.g. `ImportJob.xml`).

### Examples
**Validate Local Solution ZIP against Target Environment (Interactive Auth):**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- validate --zip "C:\Solutions\CoreSales_1_0_0_managed.zip" --url "https://prod-target.crm.dynamics.com" --interactive
```

**Validate Source Solution by Name against Target Environment:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- validate --solution "CoreSales" --src-url "https://dev-source.crm.dynamics.com" --url "https://prod-target.crm.dynamics.com" --interactive
```

---

## 2. Solution Distillation (`distill`)

Optimizes out-of-the-box (OOB) entities directly on the source server, or repairs syntax corruptions in local solution ZIP packages.

### Syntax
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- distill [Options]
```

### Parameters
*   `--url <url>`: Source environment URL for direct-to-server optimization.
*   `--solution <name>`: Solution unique name to distill.
*   `--zip <path>`: Optional local solution zip to fix XML schema corruptions.
*   `--out-zip <path>`: Output zip path (for XML corruption fixes).
*   `--simulate`: Run in simulation mode (offline).
*   `--out-diff <path>`: Destination path for the changes log (defaults to `distill_diff.json`).

### Examples
**Optimize OOB table bloat directly on the Source Dataverse Server:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- distill --solution "CoreSales" --url "https://dev-source.crm.dynamics.com" --interactive
```

**Repair local Solution ZIP XML Corruptions:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- distill --zip "C:\CorruptedSolution.zip" --out-zip "C:\RepairedSolution.zip"
```

---

## 3. Programmatic Repairs (`repair`)

Reads a validation JSON report and executes target active layer removals and source dependency additions.

### Syntax
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- repair --report <path> [Options]
```

### Parameters
*   `--report <path>`: Path to the `validation_report.json` file.
*   `--url <url>`: Target environment URL (for unmanaged layer removal).
*   `--connstr <connection-string>`: Explicit connection string for target.
*   `--src-url <url>`: Source environment URL (for missing dependency addition).
*   `--src-connstr <connection-string>`: Explicit connection string for source.
*   `--solution <name>`: Solution name on source.
*   `--interactive`: Force interactive authentication.
*   `--simulate`: Run in simulation mode (offline).

### Examples
**Perform Target & Source Repairs based on Report:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- repair --report validation_report.json --url "https://prod-target.crm.dynamics.com" --src-url "https://dev-source.crm.dynamics.com" --solution "CoreSales" --interactive
```

---

## 4. User Multienvironment Management (`role`)

Audits and manages user environment assignments, security roles, and Business Units (BUs) across multiple environments in the tenant.

### Syntax
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- role <sub-action> [Options]
```

### Sub-actions
*   `report`: Crawl environments and generate a consolidated report of user roles and Business Units.
*   `audit`: Find all users who are assigned specific roles or belong to specific Business Units.
*   `assign`: Assign a security role to users or transfer users to a new Business Unit.
*   `remove`: Remove a security role assignment from users.

### Parameters
*   `--email <list>`: Comma-separated list of target user email addresses (e.g. `user1@contoso.com,user2@contoso.com`).
*   `--role <list>`: Security role name(s) (comma-separated for audits, single name for assignments/removals).
*   `--bu <list>`: Business Unit name(s) (comma-separated for audits, single name for transfers).
*   `--env <name>`: Filter operations or reports to a single environment unique name (e.g. `contoso-dev`).
*   `--all`: Scan or apply changes across all discovered environments in the tenant.
*   `--simulate`: Run in dry-run mode (prints planned actions to the console without writing changes).
*   `--out-json <path>`: Destination path for the JSON report (defaults to `user_role_report.json`).
*   `--out-html <path>`: Destination path for the interactive HTML matrix dashboard (defaults to `user_role_report.html`).
*   `--url <url>`: Discovery environment Web API URL (used to fetch the list of environments via Global Discovery).
*   `--connstr <connection-string>`: Explicit connection string for the discovery environment.

### Examples
**Generate Consolidated User Role Matrix across All Environments (Simulation):**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- role report --email "user1@contoso.com,user2@contoso.com" --all --simulate
```

**Audit Tenant-Wide Users with the "System Administrator" Role:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- role audit --role "System Administrator" --all --interactive --url "https://my-disco-org.crm.dynamics.com"
```

**Audit Tenant-Wide Users in specific Business Units:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- role audit --bu "Sales BU,Marketing BU" --all --simulate
```

**Bulk Assign "Salesperson" Role to Users in a specific Dev Environment:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- role assign --email "user1@contoso.com,user2@contoso.com" --role "Salesperson" --env "contoso-dev" --interactive --url "https://mydisco.crm.dynamics.com"
```

**Bulk Transfer Business Unit (with role assignment) across All Environments:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- role assign --email "user@contoso.com" --role "Salesperson" --bu "Europe BU" --all --simulate
```

**Bulk Remove "Salesperson" Role across All Environments:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- role remove --email "user@contoso.com" --role "Salesperson" --all --simulate
```

