using System.Collections.Generic;

namespace Utilities.SolutionDeepValidator.Models
{
    public class SolutionManifestData
    {
        public string UniqueName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool IsManaged { get; set; }
        public List<MissingDependencyData> MissingDependencies { get; set; } = new List<MissingDependencyData>();
        public List<EntityManifestData> Entities { get; set; } = new List<EntityManifestData>();
        public List<SolutionComponentData> Components { get; set; } = new List<SolutionComponentData>();
    }

    public class MissingDependencyData
    {
        public string RequiredType { get; set; } = string.Empty;
        public string RequiredSchemaName { get; set; } = string.Empty;
        public string RequiredDisplayName { get; set; } = string.Empty;
        public string RequiredSolution { get; set; } = string.Empty;
        
        public string DependentType { get; set; } = string.Empty;
        public string DependentSchemaName { get; set; } = string.Empty;
        public string DependentDisplayName { get; set; } = string.Empty;
    }

    public class EntityManifestData
    {
        public string LogicalName { get; set; } = string.Empty;
        public bool IncludeAllComponents { get; set; }
        public List<AttributeManifestData> Attributes { get; set; } = new List<AttributeManifestData>();
    }

    public class AttributeManifestData
    {
        public string LogicalName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // e.g. nvarchar, money, decimal
        public int Length { get; set; }
    }

    public class SolutionComponentData
    {
        public string ComponentId { get; set; } = string.Empty; // GUID
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // e.g. SystemForm, WebResource
        public int ComponentType { get; set; } // Dataverse type code (e.g. 60 = SystemForm, 29 = WebResource, 29 = Workflow)
    }
}
