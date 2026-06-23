using System;
using System.Collections.Generic;

namespace Utilities.SolutionDeepValidator.Models
{
    public class TargetMetadataCache
    {
        public string OrganizationFriendlyName { get; set; } = string.Empty;
        public List<SolutionCacheItem> Solutions { get; set; } = new List<SolutionCacheItem>();
        public List<EntityCacheItem> Entities { get; set; } = new List<EntityCacheItem>();
        public Dictionary<string, List<AttributeCacheItem>> Attributes { get; set; } = new Dictionary<string, List<AttributeCacheItem>>(StringComparer.OrdinalIgnoreCase);
        public List<RelationshipCacheItem> Relationships { get; set; } = new List<RelationshipCacheItem>();
        public List<OptionSetCacheItem> OptionSets { get; set; } = new List<OptionSetCacheItem>();
        public List<WorkflowCacheItem> Workflows { get; set; } = new List<WorkflowCacheItem>();
        public List<PluginAssemblyCacheItem> PluginAssemblies { get; set; } = new List<PluginAssemblyCacheItem>();
        public List<PluginStepCacheItem> PluginSteps { get; set; } = new List<PluginStepCacheItem>();
        public List<WebResourceCacheItem> WebResources { get; set; } = new List<WebResourceCacheItem>();
        public List<SecurityRoleCacheItem> SecurityRoles { get; set; } = new List<SecurityRoleCacheItem>();
        public List<ConnectionRefCacheItem> ConnectionReferences { get; set; } = new List<ConnectionRefCacheItem>();
        
        // Metadata loading status tracking
        public HashSet<string> MetadataGaps { get; set; } = new HashSet<string>();
    }

    public class SolutionCacheItem
    {
        public string UniqueName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string PublisherId { get; set; } = string.Empty;
        public string PublisherName { get; set; } = string.Empty;
        public bool IsManaged { get; set; }
    }

    public class EntityCacheItem
    {
        public string LogicalName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsCustomizable { get; set; }
        public bool CanCreateForms { get; set; }
        public bool CanCreateViews { get; set; }
        public bool IsCustomEntity { get; set; }
    }

    public class AttributeCacheItem
    {
        public string LogicalName { get; set; } = string.Empty;
        public string AttributeType { get; set; } = string.Empty;
        public bool IsCustomizable { get; set; }
        public int MaxLength { get; set; }
        public int Precision { get; set; }
    }

    public class RelationshipCacheItem
    {
        public string SchemaName { get; set; } = string.Empty;
        public string Entity1LogicalName { get; set; } = string.Empty;
        public string Entity2LogicalName { get; set; } = string.Empty;
    }

    public class OptionSetCacheItem
    {
        public string Name { get; set; } = string.Empty;
    }

    public class WorkflowCacheItem
    {
        public string WorkflowId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PrimaryEntity { get; set; } = string.Empty;
        public int Category { get; set; } // 0 = Workflow, 3 = Action, 5 = Modern Flow, etc.
        public int StateCode { get; set; } // 0 = Draft, 1 = Activated
    }

    public class PluginAssemblyCacheItem
    {
        public string PluginAssemblyId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class PluginStepCacheItem
    {
        public string SdkMessageProcessingStepId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SdkMessageFilterId { get; set; } = string.Empty; // Bound entity logical name GUID
        public string SdkMessageName { get; set; } = string.Empty; // E.g., "Create", "Update"
        public string TargetEntity { get; set; } = string.Empty;
    }

    public class WebResourceCacheItem
    {
        public string WebResourceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class SecurityRoleCacheItem
    {
        public string RoleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class ConnectionRefCacheItem
    {
        public string ConnectionReferenceId { get; set; } = string.Empty;
        public string ConnectionReferenceLogicalName { get; set; } = string.Empty;
        public string ConnectorId { get; set; } = string.Empty;
    }
}
