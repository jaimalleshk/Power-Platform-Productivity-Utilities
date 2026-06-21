namespace Utilities.SolutionRepairDistiller.Models
{
    public class PruningTarget
    {
        public string ComponentType { get; set; } = string.Empty; // e.g. Attribute, Form, Entity
        public int ComponentTypeCode { get; set; } // Dataverse component code
        public string LogicalName { get; set; } = string.Empty;
        public string ParentTable { get; set; } = string.Empty;
        public string Guid { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
