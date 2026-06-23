using System;
using System.Collections.Generic;

namespace PowerPlatform.ProductivityEngine.Core.Reporting
{
    // --- Validator Report Models ---
    public class ValidationReport
    {
        public string SolutionName { get; set; } = string.Empty;
        public string SourceVersion { get; set; } = string.Empty;
        public string TargetEnvironment { get; set; } = string.Empty;
        public string TargetFriendlyName { get; set; } = string.Empty;
        public string SourceZipPath { get; set; } = string.Empty;
        public string UserPrincipalName { get; set; } = string.Empty;
        public DateTime ValidationTimestamp { get; set; }
        public DateTime ValidationStartTimestamp { get; set; }
        public DateTime ValidationEndTimestamp { get; set; }
        public double ValidationDurationSeconds { get; set; }
        public string OverallResult { get; set; } = string.Empty; // Passed, PassedWithWarnings, Failed
        public string ConfidenceScore { get; set; } = "High"; // High, Medium, Low
        public ValidationMetrics Metrics { get; set; } = new ValidationMetrics();
        public List<ValidationIssue> Issues { get; set; } = new List<ValidationIssue>();
        public List<string> MetadataGaps { get; set; } = new List<string>();
    }

    public class ValidationMetrics
    {
        public int TotalComponentsEvaluated { get; set; }
        public int BlockersCount { get; set; }
        public int WarningsCount { get; set; }
    }

    public class ValidationIssue
    {
        public string Id { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty; // Red (Blocker), Yellow (Warning)
        public string ComponentType { get; set; } = string.Empty;
        public string LogicalName { get; set; } = string.Empty;
        public string ParentTable { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ResolutionUrl { get; set; } = string.Empty;
        public string HelpODataQuery { get; set; } = string.Empty; // Embedded OData troubleshooting helper
    }

    // --- Distiller Report Models ---
    public class DistillerReport
    {
        public string SolutionName { get; set; } = string.Empty;
        public long OriginalFileSizeBytes { get; set; }
        public long OptimizedFileSizeBytes { get; set; }
        public double ReductionPercentage { get; set; }
        public List<PrunedComponent> ComponentsRemoved { get; set; } = new List<PrunedComponent>();
    }

    public class PrunedComponent
    {
        public string Type { get; set; } = string.Empty;
        public string LogicalName { get; set; } = string.Empty;
        public string ParentTable { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
