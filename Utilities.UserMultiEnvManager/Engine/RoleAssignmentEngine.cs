using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Connections;
using Utilities.UserMultiEnvManager.Models;

namespace Utilities.UserMultiEnvManager.Engine
{
    public class RoleAssignmentEngine
    {
        private readonly IConnectionFactory _connectionFactory;

        public RoleAssignmentEngine(IConnectionFactory? connectionFactory = null)
        {
            _connectionFactory = connectionFactory ?? new DataverseConnectionFactory();
        }

        /// <summary>
        /// Assigns a security role to target user emails across multiple environments.
        /// </summary>
        public async Task AssignRoleAsync(
            List<InstanceDto> instances,
            ConnectionProfile profile,
            List<string> emails,
            string roleName,
            bool simulate,
            Action<string>? logCallback = null)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (emails == null || emails.Count == 0) throw new ArgumentException("At least one email must be specified.", nameof(emails));
            if (string.IsNullOrWhiteSpace(roleName)) throw new ArgumentException("Role name cannot be empty.", nameof(roleName));

            string mode = simulate ? "[SIMULATION]" : "[EXECUTION]";
            logCallback?.Invoke($"{mode} Initiating role assignment for '{roleName}' across {instances.Count} environment(s)...");

            // Short-circuit simulation mode to avoid MSAL token acquisition against non-existent URLs
            if (profile.EnvironmentUrl != null && profile.EnvironmentUrl.Contains("simulation-env.crm.dynamics.com"))
            {
                foreach (var instance in instances)
                {
                    foreach (var email in emails)
                    {
                        logCallback?.Invoke($"[{instance.UniqueName}] [SIMULATION] Would assign role '{roleName}' to user '{email}'.");
                    }
                }
                return;
            }

            foreach (var instance in instances)
            {
                logCallback?.Invoke($"[{instance.UniqueName}] Connecting to environment...");
                var clientProfile = CloneProfileForUrl(profile, instance.Url);
                using var client = _connectionFactory.CreateHttpClient(clientProfile);

                foreach (var email in emails)
                {
                    try
                    {
                        // 1. Find user in the environment
                        var user = await FindUserByEmailAsync(client, email).ConfigureAwait(false);
                        if (user == null)
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] [WARNING] User '{email}' was not found in this environment. Skipping.");
                            continue;
                        }

                        if (user.IsDisabled)
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] [WARNING] User '{email}' is disabled. Proceeding with role assignment.");
                        }

                        // Check if role is already assigned
                        if (user.Roles.Any(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase)))
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] User '{email}' already possesses role '{roleName}'. Skipping.");
                            continue;
                        }

                        // 2. Resolve Role ID matching the user's Business Unit
                        Guid userBuId = user.BusinessUnit?.BusinessUnitId ?? Guid.Empty;
                        Guid roleId = await FindRoleIdByNameAndBuAsync(client, roleName, userBuId).ConfigureAwait(false);
                        
                        if (roleId == Guid.Empty)
                        {
                            // Try to find any role with that name in the environment
                            roleId = await FindAnyRoleIdByNameAsync(client, roleName).ConfigureAwait(false);
                            if (roleId == Guid.Empty)
                            {
                                logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Security Role '{roleName}' was not found in the environment. Skipping.");
                                continue;
                            }
                            logCallback?.Invoke($"[{instance.UniqueName}] Role '{roleName}' BU mismatch. Found fallback role ID {roleId}.");
                        }

                        // 3. Perform Assignment
                        if (simulate)
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] [SIMULATION] Would assign role '{roleName}' (ID: {roleId}) to user '{email}' (ID: {user.SystemUserId}).");
                        }
                        else
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] Assigning role '{roleName}' to user '{email}'...");
                            
                            string requestUri = $"systemusers({user.SystemUserId})/systemuserroles_association/$ref";

                            // Dataverse expects case-sensitive property `@odata.id`
                            // We construct a custom JsonContent or Raw String to ensure exact case.
                            string rawJson = $"{{\"@odata.id\":\"{instance.Url.TrimEnd('/')}/api/data/v9.2/roles({roleId})\"}}";
                            using var content = new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json");

                            var response = await client.PostAsync(requestUri, content).ConfigureAwait(false);
                            if (response.IsSuccessStatusCode)
                            {
                                logCallback?.Invoke($"[{instance.UniqueName}] Successfully assigned role '{roleName}' to user '{email}'.");
                            }
                            else
                            {
                                string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Failed to assign role to '{email}': {response.StatusCode} - {error}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Exception processing user '{email}': {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Removes a security role from target user emails across multiple environments.
        /// </summary>
        public async Task RemoveRoleAsync(
            List<InstanceDto> instances,
            ConnectionProfile profile,
            List<string> emails,
            string roleName,
            bool simulate,
            Action<string>? logCallback = null)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (emails == null || emails.Count == 0) throw new ArgumentException("At least one email must be specified.", nameof(emails));
            if (string.IsNullOrWhiteSpace(roleName)) throw new ArgumentException("Role name cannot be empty.", nameof(roleName));

            string mode = simulate ? "[SIMULATION]" : "[EXECUTION]";
            logCallback?.Invoke($"{mode} Initiating role removal of '{roleName}' across {instances.Count} environment(s)...");

            // Short-circuit simulation mode to avoid MSAL token acquisition against non-existent URLs
            if (profile.EnvironmentUrl != null && profile.EnvironmentUrl.Contains("simulation-env.crm.dynamics.com"))
            {
                foreach (var instance in instances)
                {
                    foreach (var email in emails)
                    {
                        logCallback?.Invoke($"[{instance.UniqueName}] [SIMULATION] Would remove role '{roleName}' from user '{email}'.");
                    }
                }
                return;
            }

            foreach (var instance in instances)
            {
                logCallback?.Invoke($"[{instance.UniqueName}] Connecting to environment...");
                var clientProfile = CloneProfileForUrl(profile, instance.Url);
                using var client = _connectionFactory.CreateHttpClient(clientProfile);

                foreach (var email in emails)
                {
                    try
                    {
                        // 1. Find user in the environment
                        var user = await FindUserByEmailAsync(client, email).ConfigureAwait(false);
                        if (user == null)
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] [WARNING] User '{email}' was not found. Skipping.");
                            continue;
                        }

                        // Check if role is assigned
                        var matchedRole = user.Roles.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
                        if (matchedRole == null)
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] User '{email}' does not have role '{roleName}'. Skipping.");
                            continue;
                        }

                        // 2. Perform Removal
                        if (simulate)
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] [SIMULATION] Would remove role '{roleName}' (ID: {matchedRole.RoleId}) from user '{email}' (ID: {user.SystemUserId}).");
                        }
                        else
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] Removing role '{roleName}' from user '{email}'...");

                            string requestUri = $"systemusers({user.SystemUserId})/systemuserroles_association/$ref?$id={instance.Url.TrimEnd('/')}/api/data/v9.2/roles({matchedRole.RoleId})";
                            var response = await client.DeleteAsync(requestUri).ConfigureAwait(false);

                            if (response.IsSuccessStatusCode)
                            {
                                logCallback?.Invoke($"[{instance.UniqueName}] Successfully removed role '{roleName}' from user '{email}'.");
                            }
                            else
                            {
                                string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Failed to remove role from '{email}': {response.StatusCode} - {error}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Exception processing user '{email}': {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Transfers a user to a different Business Unit across multiple environments, auto-assigning a valid role from the destination BU.
        /// </summary>
        public async Task SetBusinessUnitAsync(
            List<InstanceDto> instances,
            ConnectionProfile profile,
            List<string> emails,
            string buName,
            string roleName,
            bool simulate,
            Action<string>? logCallback = null)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (emails == null || emails.Count == 0) throw new ArgumentException("At least one email must be specified.", nameof(emails));
            if (string.IsNullOrWhiteSpace(buName)) throw new ArgumentException("Business Unit name cannot be empty.", nameof(buName));
            if (string.IsNullOrWhiteSpace(roleName)) throw new ArgumentException("A valid security role name from the target BU must be specified.", nameof(roleName));

            string mode = simulate ? "[SIMULATION]" : "[EXECUTION]";
            logCallback?.Invoke($"{mode} Initiating Business Unit transfer to '{buName}' (assigning role '{roleName}') across {instances.Count} environment(s)...");

            // Short-circuit simulation mode to avoid MSAL token acquisition against non-existent URLs
            if (profile.EnvironmentUrl != null && profile.EnvironmentUrl.Contains("simulation-env.crm.dynamics.com"))
            {
                foreach (var instance in instances)
                {
                    foreach (var email in emails)
                    {
                        logCallback?.Invoke($"[{instance.UniqueName}] [SIMULATION] Would transfer user '{email}' to Business Unit '{buName}' and assign role '{roleName}'.");
                    }
                }
                return;
            }

            foreach (var instance in instances)
            {
                logCallback?.Invoke($"[{instance.UniqueName}] Connecting to environment...");
                var clientProfile = CloneProfileForUrl(profile, instance.Url);
                using var client = _connectionFactory.CreateHttpClient(clientProfile);

                // 1. Resolve target Business Unit ID in this environment
                Guid buId = await FindBusinessUnitIdByNameAsync(client, buName).ConfigureAwait(false);
                if (buId == Guid.Empty)
                {
                    logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Business Unit '{buName}' was not found. Skipping environment.");
                    continue;
                }

                // 2. Resolve target Security Role ID in this environment associated with the target BU
                Guid roleId = await FindRoleIdByNameAndBuAsync(client, roleName, buId).ConfigureAwait(false);
                if (roleId == Guid.Empty)
                {
                    logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Security Role '{roleName}' belonging to Business Unit '{buName}' was not found. Skipping environment.");
                    continue;
                }

                foreach (var email in emails)
                {
                    try
                    {
                        // 3. Find user
                        var user = await FindUserByEmailAsync(client, email).ConfigureAwait(false);
                        if (user == null)
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] [WARNING] User '{email}' was not found. Skipping user.");
                            continue;
                        }

                        // Check if already in the target Business Unit
                        if (user.BusinessUnit != null && user.BusinessUnit.Name.Equals(buName, StringComparison.OrdinalIgnoreCase))
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] User '{email}' is already in Business Unit '{buName}'.");
                            
                            // Check if they need the role assigned
                            if (!user.Roles.Any(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase)))
                            {
                                logCallback?.Invoke($"[{instance.UniqueName}] User '{email}' is missing role '{roleName}' in BU. Assigning now.");
                                await AssignRoleAsync(new List<InstanceDto> { instance }, profile, new List<string> { email }, roleName, simulate, logCallback).ConfigureAwait(false);
                            }
                            continue;
                        }

                        // 4. Execute transfer
                        if (simulate)
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] [SIMULATION] Would move user '{email}' (ID: {user.SystemUserId}) to Business Unit '{buName}' (ID: {buId}) and assign role '{roleName}' (ID: {roleId}).");
                        }
                        else
                        {
                            logCallback?.Invoke($"[{instance.UniqueName}] Transferring user '{email}' to Business Unit '{buName}'...");

                            string requestUri = $"systemusers({user.SystemUserId})/Microsoft.Dynamics.CRM.SetBusinessUnit";
                            
                            // Build the case-sensitive JSON payload using raw StringContent
                            string rawJson = $"{{" +
                                             $"\"BusinessUnit\": {{\"businessunitid\":\"{buId}\"}}," +
                                             $"\"SecurityRole\": {{\"roleid\":\"{roleId}\"}}" +
                                             $"}}";
                            
                            using var content = new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json");
                            var response = await client.PostAsync(requestUri, content).ConfigureAwait(false);

                            if (response.IsSuccessStatusCode)
                            {
                                logCallback?.Invoke($"[{instance.UniqueName}] Successfully transferred user '{email}' to BU '{buName}' with role '{roleName}'.");
                            }
                            else
                            {
                                string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Failed to transfer BU for '{email}': {response.StatusCode} - {error}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"[{instance.UniqueName}] [ERROR] Exception processing user '{email}': {ex.Message}");
                    }
                }
            }
        }

        private ConnectionProfile CloneProfileForUrl(ConnectionProfile src, string url)
        {
            return new ConnectionProfile
            {
                ConnectionString = src.ConnectionString,
                EnvironmentUrl = url,
                TenantId = src.TenantId,
                ClientId = src.ClientId,
                ClientSecret = src.ClientSecret,
                ClientCertificateThumbprint = src.ClientCertificateThumbprint,
                Username = src.Username,
                Password = src.Password,
                UseInteractiveAuth = src.UseInteractiveAuth,
                RedirectUri = src.RedirectUri,
                LoginHint = src.LoginHint,
                TimeoutSeconds = src.TimeoutSeconds
            };
        }

        private async Task<SystemUserDto?> FindUserByEmailAsync(HttpClient client, string email)
        {
            string escaped = email.Replace("'", "''");
            string query = $"systemusers?$select=systemuserid,fullname,domainname,internalemailaddress,isdisabled" +
                           $"&$expand=systemuserroles_association($select=roleid,name),businessunitid($select=businessunitid,name)" +
                           $"&$filter=internalemailaddress eq '{escaped}' or domainname eq '{escaped}'";

            var response = await client.GetAsync(query).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<ODataResponse<SystemUserDto>>(content);
            return result?.Value?.FirstOrDefault();
        }

        private async Task<Guid> FindRoleIdByNameAndBuAsync(HttpClient client, string roleName, Guid buId)
        {
            string escapedRole = roleName.Replace("'", "''");
            string query = $"roles?$filter=name eq '{escapedRole}' and _businessunitid_value eq {buId}&$select=roleid";

            var response = await client.GetAsync(query).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Guid.Empty;

            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<ODataResponse<SecurityRoleDto>>(content);
            return result?.Value?.FirstOrDefault()?.RoleId ?? Guid.Empty;
        }

        private async Task<Guid> FindAnyRoleIdByNameAsync(HttpClient client, string roleName)
        {
            string escapedRole = roleName.Replace("'", "''");
            string query = $"roles?$filter=name eq '{escapedRole}'&$select=roleid";

            var response = await client.GetAsync(query).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Guid.Empty;

            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<ODataResponse<SecurityRoleDto>>(content);
            return result?.Value?.FirstOrDefault()?.RoleId ?? Guid.Empty;
        }

        private async Task<Guid> FindBusinessUnitIdByNameAsync(HttpClient client, string buName)
        {
            string escapedBu = buName.Replace("'", "''");
            string query = $"businessunits?$filter=name eq '{escapedBu}'&$select=businessunitid";

            var response = await client.GetAsync(query).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Guid.Empty;

            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<ODataResponse<BusinessUnitDto>>(content);
            return result?.Value?.FirstOrDefault()?.BusinessUnitId ?? Guid.Empty;
        }
    }
}
