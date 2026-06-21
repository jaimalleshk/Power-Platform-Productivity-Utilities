using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using PowerPlatform.ProductivityEngine.Core.Reporting;
using Utilities.SolutionDeepValidator.Models;
using Utilities.SolutionDeepValidator.Parsing;
using Utilities.SolutionDeepValidator.Validators;

namespace Core.Resilience.Tests
{
    public class ValidatorAndParserTests
    {
        [Fact]
        public void XmlValidationLogParser_ParsesXmlErrorLogs_Correctly()
        {
            // Arrange
            string mockXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ImportJob>
  <Results>
    <Result>
      <Status>failure</Status>
      <ErrorCode>0x80040203</ErrorCode>
      <ErrorText>The component was not found in the target system.</ErrorText>
      <SchemaName>new_customfield</SchemaName>
      <Type>2</Type>
    </Result>
  </Results>
</ImportJob>";

            string tempFile = Path.Combine(Path.GetTempPath(), $"ImportJob_{Guid.NewGuid():N}.xml");
            File.WriteAllText(tempFile, mockXml);

            try
            {
                var parser = new XmlValidationLogParser();

                // Act
                var issues = parser.ParseLogFile(tempFile);

                // Assert
                Assert.Single(issues);
                var issue = issues[0];
                Assert.Equal("LOG-0x80040203", issue.Id);
                Assert.Equal("Attribute", issue.ComponentType);
                Assert.Equal("new_customfield", issue.LogicalName);
                Assert.Contains("The component was not found in the target system.", issue.Description);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SolutionVersionValidator_ChecksVersionCompatibility_FailsOnDowngrade()
        {
            // Arrange
            var manifest = new SolutionManifestData
            {
                UniqueName = "TestSolution",
                Version = "1.0.0.0",
                IsManaged = true
            };

            var cache = new TargetMetadataCache();
            // Target has a newer version 2.0.0.0
            cache.Solutions.Add(new SolutionCacheItem
            {
                UniqueName = "TestSolution",
                Version = "2.0.0.0",
                IsManaged = true
            });

            var validator = new SolutionVersionValidator();

            // Act
            var issues = await validator.ValidateAsync(manifest, cache);

            // Assert
            Assert.Single(issues);
            Assert.Equal("VERSION_DOWNGRADE", issues[0].Id);
            Assert.Equal("Red", issues[0].Severity);
        }

        [Fact]
        public async Task SchemaConflictValidator_ChecksTypeAlignment_FailsOnMismatch()
        {
            // Arrange
            var manifest = new SolutionManifestData
            {
                UniqueName = "TestSolution",
                Version = "1.0.0.0"
            };
            var entity = new EntityManifestData { LogicalName = "account" };
            entity.Attributes.Add(new AttributeManifestData
            {
                LogicalName = "new_field",
                Type = "decimal" // decimal in source
            });
            manifest.Entities.Add(entity);

            var cache = new TargetMetadataCache();
            var targetAttrs = new List<AttributeCacheItem>
            {
                new AttributeCacheItem
                {
                    LogicalName = "new_field",
                    AttributeType = "Money" // money in target (not compatible with decimal)
                }
            };
            cache.Attributes["account"] = targetAttrs;

            var validator = new SchemaConflictValidator();

            // Act
            var issues = await validator.ValidateAsync(manifest, cache);

            // Assert
            Assert.Single(issues);
            Assert.Equal("ATTRIBUTE_TYPE_MISMATCH", issues[0].Id);
            Assert.Equal("Red", issues[0].Severity);
        }
    }
}
