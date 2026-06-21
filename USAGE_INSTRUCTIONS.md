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
