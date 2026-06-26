using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Connections;
using Utilities.UserMultiEnvManager.Models;

namespace Utilities.UserMultiEnvManager.Engine
{
    public class UserRoleCrawler
    {
        private readonly IConnectionFactory _connectionFactory;

        public UserRoleCrawler(IConnectionFactory? connectionFactory = null)
        {
            _connectionFactory = connectionFactory ?? new DataverseConnectionFactory();
        }

        /// <summary>
        /// Crawls a list of users by email/domain across a set of environments in parallel.
        /// </summary>
        public async Task<UserRoleReport> CrawlUsersAsync(
            List<InstanceDto> instances, 
            ConnectionProfile profile, 
            List<string> emails, 
            Action<string>? logCallback = null)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (emails == null || emails.Count == 0) throw new ArgumentException("At least one email must be specified.", nameof(emails));

            logCallback?.Invoke($"Crawling {emails.Count} user(s) across {instances.Count} environment(s) in parallel...");

            var report = new UserRoleReport
            {
                ScannedEmails = emails.Select(e => e.ToLowerInvariant()).ToList(),
                EnvironmentsScanned = instances.Select(i => i.UniqueName).ToList()
            };

            if (profile.EnvironmentUrl != null && profile.EnvironmentUrl.Contains("simulation-env.crm.dynamics.com"))
            {
                logCallback?.Invoke("[SIMULATION] Generating mock user crawler reports...");
                foreach (var email in emails)
                {
                    string target = email.ToLowerInvariant();
                    var list = new List<EnvironmentUserStatus>();
                    foreach (var inst in instances)
                    {
                        bool exists = !target.Contains("nonexistent");
                        bool isDisabled = target.Contains("disabled");
                        var roles = new List<string> { "Basic User" };
                        if (inst.UniqueName.Contains("dev"))
                        {
                            roles.Add("Environment Maker");
                            roles.Add("System Administrator");
                        }
                        else if (inst.UniqueName.Contains("test"))
                        {
                            roles.Add("Environment Maker");
                        }

                        list.Add(new EnvironmentUserStatus
                        {
                            EnvironmentUniqueName = inst.UniqueName,
                            EnvironmentUrl = inst.Url,
                            UserExists = exists,
                            IsDisabled = exists ? isDisabled : null,
                            BusinessUnitName = exists ? (inst.FriendlyName + " BU") : "",
                            Roles = exists ? roles : new List<string>()
                        });
                    }
                    report.UserStatuses[target] = list;
                }
                return report;
            }

            var userStatuses = new ConcurrentDictionary<string, ConcurrentBag<EnvironmentUserStatus>>();
            foreach (var email in emails)
            {
                userStatuses[email.ToLowerInvariant()] = new ConcurrentBag<EnvironmentUserStatus>();
            }

            var tasks = instances.Select(async instance =>
            {
                try
                {
                    logCallback?.Invoke($"[{instance.UniqueName}] Starting scan...");
                    var clientProfile = CloneProfileForUrl(profile, instance.Url);
                    using var client = _connectionFactory.CreateHttpClient(clientProfile);

                    // Build OData Filter for multiple users
                    var filterParts = new List<string>();
                    foreach (var email in emails)
                    {
                        string escaped = email.Replace("'", "''");
                        filterParts.Add($"internalemailaddress eq '{escaped}'");
                        filterParts.Add($"domainname eq '{escaped}'");
                    }
                    string filter = string.Join(" or ", filterParts);

                    string query = $"systemusers?$select=systemuserid,fullname,domainname,internalemailaddress,isdisabled" +
                                   $"&$expand=systemuserroles_association($select=roleid,name),businessunitid($select=businessunitid,name)" +
                                   $"&$filter={filter}";

                    var users = await ExecuteGetListAsync<SystemUserDto>(client, query, instance.UniqueName, logCallback).ConfigureAwait(false);

                    // Track which emails we found in this environment
                    var foundEmails = new HashSet<string>();

                    foreach (var user in users)
                    {
                        string userEmailKey = string.Empty;
                        if (!string.IsNullOrWhiteSpace(user.InternalEmailAddress))
                        {
                            userEmailKey = user.InternalEmailAddress.ToLowerInvariant();
                        }
                        else if (!string.IsNullOrWhiteSpace(user.DomainName) && user.DomainName.Contains("@"))
                        {
                            userEmailKey = user.DomainName.ToLowerInvariant();
                        }

                        if (string.IsNullOrEmpty(userEmailKey))
                            continue;

                        // Match against our searched emails list
                        foreach (var searchEmail in emails)
                        {
                            string target = searchEmail.ToLowerInvariant();
                            if (userEmailKey == target || (user.DomainName != null && user.DomainName.ToLowerInvariant() == target))
                            {
                                foundEmails.Add(target);

                                var status = new EnvironmentUserStatus
                                {
                                    EnvironmentUniqueName = instance.UniqueName,
                                    EnvironmentUrl = instance.Url,
                                    UserExists = true,
                                    IsDisabled = user.IsDisabled,
                                    BusinessUnitName = user.BusinessUnit?.Name ?? string.Empty,
                                    Roles = user.Roles.Select(r => r.Name).OrderBy(n => n).ToList()
                                };

                                if (userStatuses.TryGetValue(target, out var list))
                                {
                                    list.Add(status);
                                }
                            }
                        }
                    }

                    // For any emails NOT found in this environment, record a UserExists = false status
                    foreach (var email in emails)
                    {
                        string target = email.ToLowerInvariant();
                        if (!foundEmails.Contains(target))
                        {
                            var status = new EnvironmentUserStatus
                                {
                                    EnvironmentUniqueName = instance.UniqueName,
                                    EnvironmentUrl = instance.Url,
                                    UserExists = false,
                                    IsDisabled = null,
                                    BusinessUnitName = string.Empty,
                                    Roles = new List<string>()
                                };

                            if (userStatuses.TryGetValue(target, out var list))
                            {
                                list.Add(status);
                            }
                        }
                    }

                    logCallback?.Invoke($"[{instance.UniqueName}] Scan completed.");
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"[{instance.UniqueName}] Scan failed: {ex.Message}");
                    // Record failures as non-existent user statuses
                    foreach (var email in emails)
                    {
                        var status = new EnvironmentUserStatus
                        {
                            EnvironmentUniqueName = instance.UniqueName,
                            EnvironmentUrl = instance.Url,
                            UserExists = false,
                            IsDisabled = null,
                            BusinessUnitName = "SCAN_ERROR: " + ex.Message,
                            Roles = new List<string>()
                        };

                        if (userStatuses.TryGetValue(email.ToLowerInvariant(), out var list))
                        {
                            list.Add(status);
                        }
                    }
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Populate report
            foreach (var kvp in userStatuses)
            {
                report.UserStatuses[kvp.Key] = kvp.Value.OrderBy(s => s.EnvironmentUniqueName).ToList();
            }

            return report;
        }

        /// <summary>
        /// Audits security roles across target environments to find all assigned users in parallel.
        /// </summary>
        public async Task<RoleAuditReport> AuditRolesAsync(
            List<InstanceDto> instances, 
            ConnectionProfile profile, 
            List<string> roleNames, 
            Action<string>? logCallback = null)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (roleNames == null || roleNames.Count == 0) throw new ArgumentException("At least one role name must be specified.", nameof(roleNames));

            logCallback?.Invoke($"Auditing users holding roles ({string.Join(", ", roleNames)}) across {instances.Count} environment(s) in parallel...");

            var report = new RoleAuditReport
            {
                TargetRoles = roleNames,
                EnvironmentsScanned = instances.Select(i => i.UniqueName).ToList()
            };

            if (profile.EnvironmentUrl != null && profile.EnvironmentUrl.Contains("simulation-env.crm.dynamics.com"))
            {
                logCallback?.Invoke("[SIMULATION] Generating mock role audit matches...");
                var mockMatches = new List<AuditMatch>();
                foreach (var inst in instances)
                {
                    foreach (var role in roleNames)
                    {
                        mockMatches.Add(new AuditMatch
                        {
                            EnvironmentUniqueName = inst.UniqueName,
                            EnvironmentUrl = inst.Url,
                            TargetName = role,
                            UserName = "Admin User",
                            UserEmail = "admin@contoso.com",
                            IsDisabled = false
                        });

                        if (inst.UniqueName.Contains("dev"))
                        {
                            mockMatches.Add(new AuditMatch
                            {
                                EnvironmentUniqueName = inst.UniqueName,
                                EnvironmentUrl = inst.Url,
                                TargetName = role,
                                UserName = "Dev Lead",
                                UserEmail = "devlead@contoso.com",
                                IsDisabled = false
                            });
                        }
                    }
                }
                report.Matches = mockMatches;
                return report;
            }

            var matches = new ConcurrentBag<AuditMatch>();

            var tasks = instances.Select(async instance =>
            {
                try
                {
                    logCallback?.Invoke($"[{instance.UniqueName}] Querying security roles...");
                    var clientProfile = CloneProfileForUrl(profile, instance.Url);
                    using var client = _connectionFactory.CreateHttpClient(clientProfile);

                    var filterParts = roleNames.Select(r => $"name eq '{r.Replace("'", "''")}'");
                    string filter = string.Join(" or ", filterParts);

                    string query = $"roles?$filter={filter}&$select=roleid,name" +
                                   $"&$expand=systemuserroles_association($select=systemuserid,fullname,domainname,internalemailaddress,isdisabled)";

                    var roles = await ExecuteGetListAsync<SecurityRoleDto>(client, query, instance.UniqueName, logCallback).ConfigureAwait(false);

                    foreach (var role in roles)
                    {
                        foreach (var user in role.Users)
                        {
                            string email = !string.IsNullOrWhiteSpace(user.InternalEmailAddress) 
                                ? user.InternalEmailAddress 
                                : user.DomainName;

                            matches.Add(new AuditMatch
                            {
                                EnvironmentUniqueName = instance.UniqueName,
                                EnvironmentUrl = instance.Url,
                                TargetName = role.Name,
                                UserName = user.FullName,
                                UserEmail = email,
                                IsDisabled = user.IsDisabled
                            });
                        }
                    }
                    logCallback?.Invoke($"[{instance.UniqueName}] Security role query completed.");
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"[{instance.UniqueName}] Role audit failed: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            report.Matches = matches.OrderBy(m => m.EnvironmentUniqueName).ThenBy(m => m.TargetName).ThenBy(m => m.UserEmail).ToList();
            return report;
        }

        /// <summary>
        /// Audits Business Units across target environments to find all assigned users in parallel.
        /// </summary>
        public async Task<BuAuditReport> AuditBusAsync(
            List<InstanceDto> instances, 
            ConnectionProfile profile, 
            List<string> buNames, 
            Action<string>? logCallback = null)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (buNames == null || buNames.Count == 0) throw new ArgumentException("At least one Business Unit name must be specified.", nameof(buNames));

            logCallback?.Invoke($"Auditing users belonging to Business Units ({string.Join(", ", buNames)}) across {instances.Count} environment(s) in parallel...");

            var report = new BuAuditReport
            {
                TargetBus = buNames,
                EnvironmentsScanned = instances.Select(i => i.UniqueName).ToList()
            };

            if (profile.EnvironmentUrl != null && profile.EnvironmentUrl.Contains("simulation-env.crm.dynamics.com"))
            {
                logCallback?.Invoke("[SIMULATION] Generating mock business unit membership matches...");
                var mockMatches = new List<AuditMatch>();
                foreach (var inst in instances)
                {
                    foreach (var bu in buNames)
                    {
                        mockMatches.Add(new AuditMatch
                        {
                            EnvironmentUniqueName = inst.UniqueName,
                            EnvironmentUrl = inst.Url,
                            TargetName = bu,
                            UserName = "Sales Manager",
                            UserEmail = "salesmgr@contoso.com",
                            IsDisabled = false
                        });
                    }
                }
                report.Matches = mockMatches;
                return report;
            }

            var matches = new ConcurrentBag<AuditMatch>();

            var tasks = instances.Select(async instance =>
            {
                try
                {
                    logCallback?.Invoke($"[{instance.UniqueName}] Querying Business Unit users...");
                    var clientProfile = CloneProfileForUrl(profile, instance.Url);
                    using var client = _connectionFactory.CreateHttpClient(clientProfile);

                    var filterParts = buNames.Select(b => $"businessunitid/name eq '{b.Replace("'", "''")}'");
                    string filter = string.Join(" or ", filterParts);

                    string query = $"systemusers?$filter={filter}&$select=systemuserid,fullname,domainname,internalemailaddress,isdisabled" +
                                   $"&$expand=businessunitid($select=businessunitid,name)";

                    var users = await ExecuteGetListAsync<SystemUserDto>(client, query, instance.UniqueName, logCallback).ConfigureAwait(false);

                    foreach (var user in users)
                    {
                        string email = !string.IsNullOrWhiteSpace(user.InternalEmailAddress) 
                            ? user.InternalEmailAddress 
                            : user.DomainName;

                        matches.Add(new AuditMatch
                        {
                            EnvironmentUniqueName = instance.UniqueName,
                            EnvironmentUrl = instance.Url,
                            TargetName = user.BusinessUnit?.Name ?? string.Empty,
                            UserName = user.FullName,
                            UserEmail = email,
                            IsDisabled = user.IsDisabled
                        });
                    }
                    logCallback?.Invoke($"[{instance.UniqueName}] Business Unit query completed.");
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"[{instance.UniqueName}] Business Unit audit failed: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            report.Matches = matches.OrderBy(m => m.EnvironmentUniqueName).ThenBy(m => m.TargetName).ThenBy(m => m.UserEmail).ToList();
            return report;
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

        /// <summary>
        /// Executes a GET request and handles OData nextLink paging loops automatically.
        /// </summary>
        private async Task<List<T>> ExecuteGetListAsync<T>(
            HttpClient client, 
            string url, 
            string envName,
            Action<string>? logCallback)
        {
            var results = new List<T>();
            string? currentUrl = url;

            while (!string.IsNullOrEmpty(currentUrl))
            {
                var response = await client.GetAsync(currentUrl).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var chunk = JsonSerializer.Deserialize<ODataResponse<T>>(content);
                
                if (chunk?.Value != null)
                {
                    results.AddRange(chunk.Value);
                }

                currentUrl = chunk?.NextLink;

                // Strip base address from nextLink if it's absolute
                if (currentUrl != null && currentUrl.StartsWith(client.BaseAddress!.ToString()))
                {
                    currentUrl = currentUrl.Substring(client.BaseAddress!.ToString().Length);
                }
            }

            return results;
        }
    }
}
