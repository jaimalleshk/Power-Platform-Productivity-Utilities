using System.Collections.Generic;
using PowerPlatform.ProductivityEngine.Core.Connections;

namespace Utilities.EnvironmentComparator.Models
{
    public class ComparisonScope
    {
        public List<ConnectionProfile> TargetEnvironments { get; set; } = new();
        public bool CompareAdminSettings { get; set; } = true;
        public bool CompareOrgDbSettings { get; set; } = true;
        public bool CompareSecurityGovernance { get; set; } = true;
        public bool ComparePluginsAndSteps { get; set; } = true;
        public bool CompareEnvironmentVariables { get; set; } = true;
        public bool CompareCloudFlowsAndWorkflows { get; set; } = true;
        public bool CompareTablesAndColumns { get; set; } = true;
        public bool CompareWebResources { get; set; } = true;
        public bool CompareSecurityRoles { get; set; } = true;
        public string SearchFilter { get; set; } = string.Empty;
    }
}
