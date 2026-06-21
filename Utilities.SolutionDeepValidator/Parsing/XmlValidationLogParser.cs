using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Xml.XPath;
using PowerPlatform.ProductivityEngine.Core.Reporting;

namespace Utilities.SolutionDeepValidator.Parsing
{
    public class XmlValidationLogParser
    {
        public List<ValidationIssue> ParseLogFile(string path)
        {
            var issues = new List<ValidationIssue>();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return issues;
            }

            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".zip")
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                                {
                                    using (var s = entry.Open())
                                    {
                                        ParseXmlStream(s, entry.FullName, issues);
                                    }
                                }
                            }
                        }
                    }
                }
                else if (ext == ".xml")
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        ParseXmlStream(fs, Path.GetFileName(path), issues);
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    Id = "LOG-ERR-PARSING",
                    Severity = "Red",
                    ComponentType = "LogFile",
                    LogicalName = Path.GetFileName(path),
                    Description = $"Failed to parse log file: {ex.Message}"
                });
            }

            return issues;
        }

        private void ParseXmlStream(Stream stream, string sourceName, List<ValidationIssue> issues)
        {
            try
            {
                var doc = XDocument.Load(stream);

                // 1. Look for <ImportError> (common in some D365 log exports)
                var importErrors = doc.XPathSelectElements("//ImportError");
                foreach (var el in importErrors)
                {
                    string errorCode = el.Element("ErrorCode")?.Value ?? "LOG-001";
                    string description = el.Element("Description")?.Value ?? el.Element("ErrorText")?.Value ?? "Import error";
                    string schemaName = el.Element("SchemaName")?.Value ?? el.Element("Name")?.Value ?? string.Empty;
                    string typeCodeStr = el.Element("Type")?.Value ?? "Component";

                    string componentType = typeCodeStr;
                    if (int.TryParse(typeCodeStr, out int typeCode))
                    {
                        componentType = MapComponentTypeCode(typeCode);
                    }

                    issues.Add(new ValidationIssue
                    {
                        Id = errorCode.StartsWith("0x") ? $"LOG-{errorCode}" : errorCode,
                        Severity = "Red",
                        ComponentType = componentType,
                        LogicalName = schemaName,
                        Description = $"D365 Log Error ({sourceName}): {description}"
                    });
                }

                // 2. Look for <Result> nodes under <Results> (from ImportJob.xml)
                var results = doc.XPathSelectElements("//Result");
                foreach (var el in results)
                {
                    string status = el.Element("Status")?.Value ?? string.Empty;
                    if (status.Equals("failure", StringComparison.OrdinalIgnoreCase) || 
                        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                        el.Element("ErrorCode") != null)
                    {
                        string errorCode = el.Element("ErrorCode")?.Value ?? "LOG-002";
                        string errorText = el.Element("ErrorText")?.Value ?? el.Element("Description")?.Value ?? "Unknown failure";
                        string schemaName = el.Element("SchemaName")?.Value ?? el.Element("Name")?.Value ?? string.Empty;
                        string typeCodeStr = el.Element("Type")?.Value ?? "Component";

                        string componentType = typeCodeStr;
                        if (int.TryParse(typeCodeStr, out int typeCode))
                        {
                            componentType = MapComponentTypeCode(typeCode);
                        }

                        issues.Add(new ValidationIssue
                        {
                            Id = errorCode.StartsWith("0x") ? $"LOG-{errorCode}" : errorCode,
                            Severity = "Red",
                            ComponentType = componentType,
                            LogicalName = schemaName,
                            Description = $"D365 Log Failure ({sourceName}): {errorText}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue
                {
                    Id = "LOG-ERR-XML",
                    Severity = "Red",
                    ComponentType = "XMLParser",
                    LogicalName = sourceName,
                    Description = $"Failed to read XML document in log: {ex.Message}"
                });
            }
        }

        private static string MapComponentTypeCode(int code)
        {
            return code switch
            {
                1 => "Entity",
                2 => "Attribute",
                3 => "Relationship",
                60 => "SystemForm",
                29 => "Workflow",
                61 => "WebResource",
                9 => "OptionSet",
                _ => $"ComponentType_{code}"
            };
        }
    }
}
