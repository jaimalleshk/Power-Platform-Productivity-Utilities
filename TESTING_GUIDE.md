# Testing Guide: Power Platform Productivity Engine

This document describes how to execute automated unit tests and run simulation runs, including screenshots of the expected CLI outputs and reporting dashboards.

---

## 1. Running Unit Tests

Automated testing targets critical engine components, including:
*   HTTP 429 throttling / retry resilience policies.
*   The OData target metadata crawler recursion.
*   In-memory solution XML corruption regex repairs.
*   Individual validation rules (downgrade risks, type conversions, naming conflicts).

### Command
```powershell
dotnet test
```

### Expected Output
```text
Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 4 s - Core.Resilience.Tests.dll (net9.0)
```

---

## 2. Simulation Runs (Offline Mocks)

If you do not have active source/target environments connected, you can run all commands in simulation mode using the `--simulate` flag. The system simulates two distinct environment layers (Source and Target) containing preconfigured components, unmanaged active layers, and dependencies.

### Command
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- validate --simulate
```

### CLI Output Screenshot
Below is a screenshot of the dark terminal console during a simulation validation run, showing colorized logs and status updates:

![CLI Terminal Simulation Output Screenshot](file:///C:/Users/jaima/.gemini/antigravity/brain/9f1ff5f5-ea84-4c0f-a8e1-e5ca8096eb37/cli_simulation_screenshot_1782009023380.png)

---

## 3. Interactive Reports

After a validation run, the utility generates a premium HTML dashboard report. This file is self-contained and can be opened on any web browser.

### HTML Dashboard Screenshot
Below is a screenshot of the interactive web dashboard displaying the confidence gauge, severity status tags, issue descriptions, and OData repair queries:

![HTML Dashboard Mockup Screenshot](file:///C:/Users/jaima/.gemini/antigravity/brain/9f1ff5f5-ea84-4c0f-a8e1-e5ca8096eb37/html_report_dashboard_1782009031229.png)

---

## 4. Distillation & Repair Simulation Commands

To test server-side pruning and programmatic unmanaged layer deletions:

**Distill OOB Table Bloat:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- distill --simulate
```

**Execute Target/Source Repairs:**
```powershell
dotnet run --project PowerPlatform.ProductivityEngine.ConsoleUX -- repair --report validation_report.json --src-url https://source.crm.dynamics.com --solution EnterpriseCoreModifications --simulate
```
