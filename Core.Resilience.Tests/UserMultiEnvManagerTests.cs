using System;
using System.Collections.Generic;
using System.IO;
using Utilities.UserMultiEnvManager.Engine;
using Utilities.UserMultiEnvManager.Models;
using Xunit;

namespace Core.Resilience.Tests
{
    public class UserMultiEnvManagerTests
    {
        [Fact]
        public void ExportUserRoleReport_CreatesValidJsonAndHtml()
        {
            // Arrange
            string tempHtmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.html");
            string tempJsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

            var report = new UserRoleReport
            {
                ScannedEmails = new List<string> { "testuser@contoso.com", "admin@contoso.com" },
                EnvironmentsScanned = new List<string> { "contoso-dev", "contoso-prod" },
                UserStatuses = new Dictionary<string, List<EnvironmentUserStatus>>
                {
                    ["testuser@contoso.com"] = new List<EnvironmentUserStatus>
                    {
                        new EnvironmentUserStatus
                        {
                            EnvironmentUniqueName = "contoso-dev",
                            EnvironmentUrl = "https://contoso-dev.crm.dynamics.com",
                            UserExists = true,
                            IsDisabled = false,
                            BusinessUnitName = "Contoso Dev BU",
                            Roles = new List<string> { "Basic User", "Environment Maker" }
                        },
                        new EnvironmentUserStatus
                        {
                            EnvironmentUniqueName = "contoso-prod",
                            EnvironmentUrl = "https://contoso-prod.crm.dynamics.com",
                            UserExists = false,
                            IsDisabled = null,
                            BusinessUnitName = "",
                            Roles = new List<string>()
                        }
                    },
                    ["admin@contoso.com"] = new List<EnvironmentUserStatus>
                    {
                        new EnvironmentUserStatus
                        {
                            EnvironmentUniqueName = "contoso-dev",
                            EnvironmentUrl = "https://contoso-dev.crm.dynamics.com",
                            UserExists = true,
                            IsDisabled = false,
                            BusinessUnitName = "Contoso Dev BU",
                            Roles = new List<string> { "System Administrator" }
                        },
                        new EnvironmentUserStatus
                        {
                            EnvironmentUniqueName = "contoso-prod",
                            EnvironmentUrl = "https://contoso-prod.crm.dynamics.com",
                            UserExists = true,
                            IsDisabled = false,
                            BusinessUnitName = "Contoso Prod BU",
                            Roles = new List<string> { "System Administrator" }
                        }
                    }
                }
            };

            var comparer = new RoleComparer();

            try
            {
                // Act
                comparer.ExportReportToJson(tempJsonPath, report);
                comparer.ExportUserRoleReportToHtml(tempHtmlPath, report);

                // Assert
                Assert.True(File.Exists(tempJsonPath));
                Assert.True(File.Exists(tempHtmlPath));

                string jsonContent = File.ReadAllText(tempJsonPath);
                string htmlContent = File.ReadAllText(tempHtmlPath);

                Assert.Contains("testuser@contoso.com", jsonContent);
                Assert.Contains("contoso-dev", jsonContent);
                Assert.Contains("System Administrator", jsonContent);

                Assert.Contains("Tenant User Roles & BU Alignment", htmlContent);
                Assert.Contains("testuser@contoso.com", htmlContent);
                Assert.Contains("contoso-dev", htmlContent);
            }
            finally
            {
                // Cleanup
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
                if (File.Exists(tempHtmlPath)) File.Delete(tempHtmlPath);
            }
        }

        [Fact]
        public void ExportRoleAuditReport_CreatesValidHtml()
        {
            // Arrange
            string tempHtmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.html");
            string tempJsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

            var report = new RoleAuditReport
            {
                TargetRoles = new List<string> { "System Administrator" },
                EnvironmentsScanned = new List<string> { "contoso-dev" },
                Matches = new List<AuditMatch>
                {
                    new AuditMatch
                    {
                        EnvironmentUniqueName = "contoso-dev",
                        EnvironmentUrl = "https://contoso-dev.crm.dynamics.com",
                        TargetName = "System Administrator",
                        UserName = "Global Admin",
                        UserEmail = "admin@contoso.com",
                        IsDisabled = false
                    }
                }
            };

            var comparer = new RoleComparer();

            try
            {
                // Act
                comparer.ExportReportToJson(tempJsonPath, report);
                comparer.ExportRoleAuditReportToHtml(tempHtmlPath, report);

                // Assert
                Assert.True(File.Exists(tempJsonPath));
                Assert.True(File.Exists(tempHtmlPath));

                string jsonContent = File.ReadAllText(tempJsonPath);
                string htmlContent = File.ReadAllText(tempHtmlPath);

                Assert.Contains("System Administrator", jsonContent);
                Assert.Contains("admin@contoso.com", jsonContent);

                Assert.Contains("Security Role Compliance Audit Report", htmlContent);
                Assert.Contains("admin@contoso.com", htmlContent);
            }
            finally
            {
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
                if (File.Exists(tempHtmlPath)) File.Delete(tempHtmlPath);
            }
        }

        [Fact]
        public void ExportBuAuditReport_CreatesValidHtml()
        {
            // Arrange
            string tempHtmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.html");
            string tempJsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");

            var report = new BuAuditReport
            {
                TargetBus = new List<string> { "Contoso North", "Contoso South" },
                EnvironmentsScanned = new List<string> { "contoso-dev" },
                Matches = new List<AuditMatch>
                {
                    new AuditMatch
                    {
                        EnvironmentUniqueName = "contoso-dev",
                        EnvironmentUrl = "https://contoso-dev.crm.dynamics.com",
                        TargetName = "Contoso North",
                        UserName = "Test User",
                        UserEmail = "testuser@contoso.com",
                        IsDisabled = false
                    },
                    new AuditMatch
                    {
                        EnvironmentUniqueName = "contoso-dev",
                        EnvironmentUrl = "https://contoso-dev.crm.dynamics.com",
                        TargetName = "Contoso South",
                        UserName = "Admin User",
                        UserEmail = "admin@contoso.com",
                        IsDisabled = true
                    }
                }
            };

            var comparer = new RoleComparer();

            try
            {
                // Act
                comparer.ExportReportToJson(tempJsonPath, report);
                comparer.ExportBuAuditReportToHtml(tempHtmlPath, report);

                // Assert
                Assert.True(File.Exists(tempJsonPath));
                Assert.True(File.Exists(tempHtmlPath));

                string jsonContent = File.ReadAllText(tempJsonPath);
                string htmlContent = File.ReadAllText(tempHtmlPath);

                Assert.Contains("Contoso North", jsonContent);
                Assert.Contains("testuser@contoso.com", jsonContent);

                Assert.Contains("Business Unit Membership Compliance Report", htmlContent);
                Assert.Contains("testuser@contoso.com", htmlContent);
                Assert.Contains("admin@contoso.com", htmlContent);
            }
            finally
            {
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
                if (File.Exists(tempHtmlPath)) File.Delete(tempHtmlPath);
            }
        }
    }
}
