using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Utilities.UserMultiEnvManager.Models
{
    public class DiscoveryResponse
    {
        [JsonPropertyName("value")]
        public List<InstanceDto> Value { get; set; } = new();
    }

    public class InstanceDto
    {
        [JsonPropertyName("Id")]
        public Guid Id { get; set; }

        [JsonPropertyName("UniqueName")]
        public string UniqueName { get; set; } = string.Empty;

        [JsonPropertyName("FriendlyName")]
        public string FriendlyName { get; set; } = string.Empty;

        [JsonPropertyName("State")]
        public int State { get; set; }

        [JsonPropertyName("Url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("ApiUrl")]
        public string ApiUrl { get; set; } = string.Empty;
    }

    public class ODataResponse<T>
    {
        [JsonPropertyName("@odata.context")]
        public string Context { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = new();

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    public class SystemUserDto
    {
        [JsonPropertyName("systemuserid")]
        public Guid SystemUserId { get; set; }

        [JsonPropertyName("fullname")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("domainname")]
        public string DomainName { get; set; } = string.Empty;

        [JsonPropertyName("internalemailaddress")]
        public string InternalEmailAddress { get; set; } = string.Empty;

        [JsonPropertyName("isdisabled")]
        public bool IsDisabled { get; set; }

        [JsonPropertyName("systemuserroles_association")]
        public List<SecurityRoleDto> Roles { get; set; } = new();

        [JsonPropertyName("businessunitid")]
        public BusinessUnitDto? BusinessUnit { get; set; }
    }

    public class SecurityRoleDto
    {
        [JsonPropertyName("roleid")]
        public Guid RoleId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("_businessunitid_value")]
        public Guid? BusinessUnitId { get; set; }

        [JsonPropertyName("systemuserroles_association")]
        public List<SystemUserDto> Users { get; set; } = new();
    }

    public class BusinessUnitDto
    {
        [JsonPropertyName("businessunitid")]
        public Guid BusinessUnitId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class EnvironmentUserStatus
    {
        public string EnvironmentUniqueName { get; set; } = string.Empty;
        public string EnvironmentUrl { get; set; } = string.Empty;
        public bool UserExists { get; set; }
        public bool? IsDisabled { get; set; }
        public string BusinessUnitName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }

    public class UserRoleReport
    {
        public List<string> ScannedEmails { get; set; } = new();
        public List<string> EnvironmentsScanned { get; set; } = new();
        public Dictionary<string, List<EnvironmentUserStatus>> UserStatuses { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class AuditMatch
    {
        public string EnvironmentUniqueName { get; set; } = string.Empty;
        public string EnvironmentUrl { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty; // Role name or BU name
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public bool IsDisabled { get; set; }
    }

    public class RoleAuditReport
    {
        public List<string> TargetRoles { get; set; } = new();
        public List<string> EnvironmentsScanned { get; set; } = new();
        public List<AuditMatch> Matches { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class BuAuditReport
    {
        public List<string> TargetBus { get; set; } = new();
        public List<string> EnvironmentsScanned { get; set; } = new();
        public List<AuditMatch> Matches { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
