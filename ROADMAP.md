# Project Roadmap and Milestones

This document tracks the completed features, active milestones, and upcoming requirements for the **Power Platform Productivity Engine** project.

---

## Milestone 1: Library-Based Architecture & Deep Validation (Completed)

This milestone focused on decoupling the application logic from the Console user interface, ensuring that the utility logic can be reused in any environment (including CLI, Desktop, Mobile, or Web).

### Key Features Delivered

1. **Reusable Class Libraries (DLLs)**:
   - **`PowerPlatform.ProductivityEngine.Core.dll`**: Handles connection management, multi-token MSAL caches, Semaphore-locked HTTP 429 rate limit resilience, and HTML/JSON reporting.
   - **`Utilities.SolutionDeepValidator.dll`**: Extracts solution ZIPs in-memory, crawls 11 target metadata sources, and runs 19 validators.
   - **`Utilities.SolutionRepairDistiller.dll`**: Solves direct-to-server bloat distillation, local XML corruption preprocessing, and automated repair executions.

2. **Decoupled Progress Reporting (`IProgress<ProgressUpdate>`)**:
   - Every library operation exposes a progress reporting callback, enabling UI consumers to receive stage names, status levels (Info, Warning, Success, Error), progress percentages, and real-time execution messages.

3. **19 Deep Validation Checkers**:
   - Covers: Solution versions (`PENDING_UPGRADE`, `VERSION_DOWNGRADE`, `MANAGED_INTO_UNMANAGED`), missing dependencies, schema mismatches (attribute type conflicts, length reductions, precision changes), managed property locks, publisher prefix overlaps, workflow parameters, plugin step entities, web resource existence, sitemap routes, and connection reference types.

4. **Target Environment Metadata Cache**:
   - Implements sequential OData queries across 11 metadata tables.
   - Enforces a preferred page size of 5,000 using `@odata.nextLink` recursion loops to prevent query timeouts.

5. **Local XML preprocessor & Sanitizer**:
   - Uses regex-based stripping of invalid control characters, deduplicates namespace declarations, and repairs malformed tag braces in local solution packages prior to XML validation log parsing.

6. **Programmatic Dataverse Repairs**:
   - Target repairs: Removes unmanaged active layers (`RemoveActiveCustomizations`) from forms, workflows, and web resources.
   - Source repairs: Automates missing dependency additions (`AddSolutionComponent`) and solution exports.

---

## Milestone 2: Multi-Platform UI Clients (Upcoming)

*Target: Q3 2026*

Develop native user interfaces that consume the Core and Engine libraries directly.

- [ ] **Native Desktop & Mobile Applications**:
  - Build a multi-platform utility application using **.NET MAUI** targeting Windows and macOS.
  - Implement interactive dashboards displaying validation charts and status logs.
  - Leverage local SQLite for caching credentials, environment targets, and historical validation reports.
- [ ] **Web Dashboard**:
  - Develop a **Blazor Web App** connecting to an Azure SQL DB.
  - Provide a central administrative portal for scheduling routine environment scans and reviewing organizational compliance metrics.

---

## Milestone 3: DevOps & CI/CD Pipeline Integrations (Upcoming)

*Target: Q4 2026*

Integrate validation checks directly into ALM (Application Lifecycle Management) processes.

- [ ] **GitHub Action & Azure DevOps Tasks**:
  - Package the `SolutionDeepValidator` library into a custom task/action.
  - Automatically run validation scans on solution ZIPs uploaded during pull requests.
  - Block merge operations if the validation confidence score is `Low` or blockers are detected.
- [ ] **Automatic Pull Request Feedback**:
  - Post summary comments directly back to the pull request with warning flags and HTML dashboard links.

---

## Milestone 4: Extensible Validation Rules (JSON Ruleset Engine) (Upcoming)

*Target: Q1 2027*

Decouple validation rules from compiled C# code, allowing custom rulesets to be defined dynamically.

- [ ] **JSON Ruleset Schema**:
  - Allow users to write custom validator rules in JSON format (e.g. check for prohibited solution components, block specific publishers, or enforce naming conventions).
- [ ] **Dynamic Rule Loader**:
  - Add support for loading and executing local or remote JSON rulesets at runtime.

---

## Milestone 5: Advanced Repair Simulations & Dry-Run (Upcoming)

*Target: Q2 2027*

Improve safety during repair operations by giving operators greater control.

- [ ] **Dry-Run Mode & Interactive Diff**:
  - Generate a detailed repair plan showing exactly which components, workflows, and active layers will be touched, deleted, or added before executing changes.
- [ ] **Rollback Points**:
  - Automatically export a backup of target solutions and active layers before performing any delete or update repairs, enabling a quick rollback if something fails.
