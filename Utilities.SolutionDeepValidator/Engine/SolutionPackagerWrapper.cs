using System;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Collections.Generic;
using Utilities.SolutionDeepValidator.Models;

namespace Utilities.SolutionDeepValidator.Engine
{
    public class SolutionPackagerWrapper
    {
        public SolutionManifestData ParseSolutionZip(byte[] zipBytes)
        {
            using (var ms = new MemoryStream(zipBytes))
            {
                return ParseSolutionZip(ms);
            }
        }

        public SolutionManifestData ParseSolutionZip(Stream zipStream)
        {
            var manifestData = new SolutionManifestData();

            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                // 1. Parse solution.xml
                var solutionEntry = archive.GetEntry("solution.xml");
                if (solutionEntry == null)
                {
                    throw new FileNotFoundException("solution.xml not found inside the solution zip package.");
                }

                using (var entryStream = solutionEntry.Open())
                {
                    ParseSolutionXml(entryStream, manifestData);
                }

                // 2. Parse customizations.xml
                var customizationsEntry = archive.GetEntry("customizations.xml");
                if (customizationsEntry != null)
                {
                    using (var entryStream = customizationsEntry.Open())
                    {
                        ParseCustomizationsXml(entryStream, manifestData);
                    }
                }

                // 3. Parse security role files to populate Role Names
                ParseRoleFiles(archive, manifestData);
            }

            return manifestData;
        }

        private void ParseRoleFiles(ZipArchive archive, SolutionManifestData data)
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith("Roles/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    using (var stream = entry.Open())
                    {
                        var doc = XDocument.Load(stream);
                        var root = doc.Root;
                        if (root != null)
                        {
                            string id = root.Attribute("id")?.Value ?? string.Empty;
                            string name = root.Attribute("name")?.Value ?? string.Empty;

                            if (string.IsNullOrEmpty(id))
                            {
                                id = root.Element("roleid")?.Value ?? string.Empty;
                            }
                            if (string.IsNullOrEmpty(name))
                            {
                                name = root.Element("name")?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(name))
                                {
                                    name = root.Element("localizednames")
                                        ?.XPathSelectElement("localizedname[@languagecode='1033']")
                                        ?.Attribute("description")?.Value ?? string.Empty;
                                }
                            }

                            id = id.Trim('{', '}');

                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            {
                                var component = data.Components.Find(c => 
                                    c.ComponentType == 20 && 
                                    Guid.TryParse(c.ComponentId, out var cGuid) &&
                                    Guid.TryParse(id, out var idGuid) &&
                                    cGuid == idGuid);

                                if (component != null)
                                {
                                    component.Name = name;
                                }
                                else
                                {
                                    data.Components.Add(new SolutionComponentData
                                    {
                                        ComponentId = id,
                                        Name = name,
                                        Type = "Role",
                                        ComponentType = 20
                                    });
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore unparseable/corrupted role files
                }
            }
        }

        private void ParseSolutionXml(Stream stream, SolutionManifestData data)
        {
            var doc = XDocument.Load(stream);
            
            // Unique Name and Version
            var uniqueNameEl = doc.XPathSelectElement("/ImportExportXml/SolutionManifest/UniqueName");
            if (uniqueNameEl != null)
            {
                data.UniqueName = uniqueNameEl.Value;
            }

            var versionEl = doc.XPathSelectElement("/ImportExportXml/SolutionManifest/Version");
            if (versionEl != null)
            {
                data.Version = versionEl.Value;
            }
            else
            {
                var rootVersionAttr = doc.Root?.Attribute("Version");
                if (rootVersionAttr != null)
                {
                    data.Version = rootVersionAttr.Value;
                }
            }

            var managedEl = doc.XPathSelectElement("/ImportExportXml/SolutionManifest/Managed");
            if (managedEl != null)
            {
                data.IsManaged = managedEl.Value == "1";
            }

            // Missing Dependencies
            var depElements = doc.XPathSelectElements("/ImportExportXml/MissingDependencies/MissingDependency");
            foreach (var el in depElements)
            {
                var reqEl = el.Element("Required");
                var depEl = el.Element("Dependent");

                if (reqEl != null && depEl != null)
                {
                    data.MissingDependencies.Add(new MissingDependencyData
                    {
                        RequiredType = reqEl.Attribute("type")?.Value ?? string.Empty,
                        RequiredSchemaName = reqEl.Attribute("schemaName")?.Value ?? string.Empty,
                        RequiredDisplayName = reqEl.Attribute("displayName")?.Value ?? string.Empty,
                        RequiredSolution = reqEl.Attribute("solution")?.Value ?? string.Empty,

                        DependentType = depEl.Attribute("type")?.Value ?? string.Empty,
                        DependentSchemaName = depEl.Attribute("schemaName")?.Value ?? string.Empty,
                        DependentDisplayName = depEl.Attribute("displayName")?.Value ?? string.Empty
                    });
                }
            }

            // Also parse components defined in RootComponents (if present)
            var rootCompElements = doc.XPathSelectElements("/ImportExportXml/SolutionManifest/RootComponents/RootComponent");
            foreach (var el in rootCompElements)
            {
                string id = el.Attribute("id")?.Value ?? string.Empty;
                string schemaName = el.Attribute("schemaName")?.Value ?? string.Empty;
                string typeStr = el.Attribute("type")?.Value ?? "0";
                
                if (!string.IsNullOrEmpty(id) && int.TryParse(typeStr, out int compType))
                {
                    // Map component type code to user-friendly type name
                    string typeName = MapComponentTypeCode(compType);
                    
                    // Only add if not already parsed
                    if (!data.Components.Exists(c => c.ComponentId.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    {
                        data.Components.Add(new SolutionComponentData
                        {
                            ComponentId = id,
                            Name = schemaName,
                            Type = typeName,
                            ComponentType = compType
                        });
                    }
                }
            }
        }

        private void ParseCustomizationsXml(Stream stream, SolutionManifestData data)
        {
            var doc = XDocument.Load(stream);
            
            // Extract Entities
            var entityElements = doc.XPathSelectElements("/ImportExportXml/Entities/Entity");
            foreach (var el in entityElements)
            {
                string logicalName = el.Attribute("Name")?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(logicalName)) continue;

                bool includeAll = false;
                var includeAllAttr = el.Attribute("IncludeAllComponents");
                if (includeAllAttr != null && bool.TryParse(includeAllAttr.Value, out bool val))
                {
                    includeAll = val;
                }

                var entityData = new EntityManifestData
                {
                    LogicalName = logicalName,
                    IncludeAllComponents = includeAll
                };

                // Extract custom attributes details in this solution
                var attributeNodes = el.XPathSelectElements("EntityInfo/Attributes/Attribute");
                foreach (var attrNode in attributeNodes)
                {
                    string attrName = attrNode.Attribute("PhysicalName")?.Value ?? string.Empty;
                    if (string.IsNullOrEmpty(attrName)) continue;

                    string typeStr = attrNode.Element("Type")?.Value ?? string.Empty;
                    int lengthVal = 0;
                    var lengthEl = attrNode.Element("Length");
                    if (lengthEl != null)
                    {
                        int.TryParse(lengthEl.Value, out lengthVal);
                    }

                    entityData.Attributes.Add(new AttributeManifestData
                    {
                        LogicalName = attrName,
                        Type = typeStr,
                        Length = lengthVal
                    });
                }

                data.Entities.Add(entityData);
            }

            // Extract SystemForms (Type 60)
            var formElements = doc.XPathSelectElements("/ImportExportXml/Forms/systemform");
            foreach (var el in formElements)
            {
                var formIdEl = el.Element("formid");
                var nameEl = el.Element("localizednames")?.XPathSelectElement("localizedname[@languagecode='1033']");
                string formId = formIdEl?.Value ?? string.Empty;
                string formName = nameEl?.Attribute("description")?.Value ?? formId;

                if (!string.IsNullOrEmpty(formId))
                {
                    AddComponentIfNew(data, formId, formName, "SystemForm", 60);
                }
            }

            // Extract Workflows / Processes (Type 29)
            var workflowElements = doc.XPathSelectElements("/ImportExportXml/Workflows/Workflow");
            foreach (var el in workflowElements)
            {
                string workflowId = el.Attribute("WorkflowId")?.Value ?? string.Empty;
                string workflowName = el.Attribute("Name")?.Value ?? workflowId;

                if (!string.IsNullOrEmpty(workflowId))
                {
                    AddComponentIfNew(data, workflowId, workflowName, "Workflow", 29);
                }
            }

            // Extract WebResources (Type 61)
            var webResourceElements = doc.XPathSelectElements("/ImportExportXml/WebResources/WebResource");
            foreach (var el in webResourceElements)
            {
                var wrIdEl = el.Element("WebResourceId");
                var nameEl = el.Element("Name");
                string wrId = wrIdEl?.Value ?? string.Empty;
                string wrName = nameEl?.Value ?? wrId;

                if (!string.IsNullOrEmpty(wrId))
                {
                    AddComponentIfNew(data, wrId, wrName, "WebResource", 61);
                }
            }
        }

        private void AddComponentIfNew(SolutionManifestData data, string id, string name, string type, int typeCode)
        {
            if (!data.Components.Exists(c => c.ComponentId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                data.Components.Add(new SolutionComponentData
                {
                    ComponentId = id,
                    Name = name,
                    Type = type,
                    ComponentType = typeCode
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
                90 => "PluginAssembly",
                92 => "PluginStep",
                20 => "Role",
                10047 => "ConnectionReference",
                10243 => "AppAction",
                62 => "SiteMap",
                63 => "RibbonCustomization",
                380 => "EnvironmentVariableDefinition",
                300 => "CanvasApp",
                _ => $"Component_{code}"
            };
        }
    }
}
