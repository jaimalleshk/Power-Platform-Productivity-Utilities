using System;
using System.Collections.Generic;
using System.Linq;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine
{
    public class NWayComparer
    {
        public ComparisonResult CompareEnvironments(List<RawEnvData> envDataList, ComparisonScope scope)
        {
            var result = new ComparisonResult
            {
                TargetEnvironmentNames = envDataList.Select(e => e.EnvironmentName).ToList(),
                ComparedAt = DateTime.UtcNow
            };

            // 1. Process Admin Settings & Environment Variables (Root 1)
            var adminKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var env in envDataList)
            {
                foreach (var k in env.AdminSettings.Keys) adminKeys.Add(k);
            }

            foreach (var key in adminKeys.OrderBy(k => k))
            {
                var node = BuildNode(key, RootCategory.AdminSettings, envDataList, (env) => 
                    env.AdminSettings.TryGetValue(key, out var dict) ? dict : null);

                if (MatchesFilter(node, scope))
                {
                    result.AdminSettingsNodes.Add(node);
                    IncrementStats(result, node.Status);
                }
            }

            // 2. Process Metadata & Customizations (Root 2 - Solution Explorer Tree Hierarchy)
            var metadataKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var env in envDataList)
            {
                foreach (var k in env.MetadataItems.Keys) metadataKeys.Add(k);
            }

            var flatNodes = new List<DiffNode>();
            foreach (var key in metadataKeys.OrderBy(k => k))
            {
                var node = BuildNode(key, RootCategory.MetadataCustomizations, envDataList, (env) => 
                    env.MetadataItems.TryGetValue(key, out var dict) ? dict : null);

                if (MatchesFilter(node, scope))
                {
                    flatNodes.Add(node);
                    IncrementStats(result, node.Status);
                }
            }

            result.MetadataNodes = BuildSolutionExplorerTree(flatNodes);

            return result;
        }

        private List<DiffNode> BuildSolutionExplorerTree(List<DiffNode> flatNodes)
        {
            var rootNodes = new List<DiffNode>();

            // Group 0A: Installed Solutions & First-Party Packages (Expandable with OOB Component Trees)
            var solNodes = flatNodes.Where(n => n.SubCategory.Equals("Solution", StringComparison.OrdinalIgnoreCase)).ToList();
            if (solNodes.Count > 0)
            {
                var solFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "📁 Installed Solutions & First-Party Packages",
                    UniqueKey = "Folder.Solutions"
                };

                foreach (var solNode in solNodes)
                {
                    // Build Expandable Solution Component Tree under each Solution
                    var solComponentTree = BuildComponentSubTreeForSolution(solNode, flatNodes);
                    solFolder.Children.Add(solComponentTree);
                }

                rootNodes.Add(solFolder);
            }

            // Group 0B: Installed D365 Apps, Canvas Apps, & Custom Pages
            var appNodes = flatNodes.Where(n => n.SubCategory.Equals("InstalledApp", StringComparison.OrdinalIgnoreCase) || 
                                                n.SubCategory.Equals("CanvasApp", StringComparison.OrdinalIgnoreCase) ||
                                                n.SubCategory.Equals("CustomPage", StringComparison.OrdinalIgnoreCase)).ToList();
            if (appNodes.Count > 0)
            {
                var appFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "📱 Model-Driven Apps, Canvas Apps, & Custom Pages",
                    UniqueKey = "Folder.Apps"
                };
                foreach (var n in appNodes) appFolder.Children.Add(n);
                rootNodes.Add(appFolder);
            }

            // Group 0C: System & Interactive Dashboards
            var dashNodes = flatNodes.Where(n => n.SubCategory.StartsWith("Dashboard", StringComparison.OrdinalIgnoreCase)).ToList();
            if (dashNodes.Count > 0)
            {
                var dashFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "📊 System Dashboards & User Dashboards",
                    UniqueKey = "Folder.Dashboards"
                };
                foreach (var n in dashNodes) dashFolder.Children.Add(n);
                rootNodes.Add(dashFolder);
            }

            // Group 0D: PCF Controls (PowerApps Component Framework)
            var pcfNodes = flatNodes.Where(n => n.SubCategory.StartsWith("PcfControl", StringComparison.OrdinalIgnoreCase)).ToList();
            if (pcfNodes.Count > 0)
            {
                var pcfFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "🧩 PCF Controls (PowerApps Component Framework)",
                    UniqueKey = "Folder.PcfControls"
                };
                foreach (var n in pcfNodes) pcfFolder.Children.Add(n);
                rootNodes.Add(pcfFolder);
            }

            // Group 0E: Site Maps & Navigation Menus
            var sitemapNodes = flatNodes.Where(n => n.SubCategory.StartsWith("SiteMap", StringComparison.OrdinalIgnoreCase)).ToList();
            if (sitemapNodes.Count > 0)
            {
                var sitemapFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "🧭 Site Maps & Application Navigation",
                    UniqueKey = "Folder.SiteMaps"
                };
                foreach (var n in sitemapNodes) sitemapFolder.Children.Add(n);
                rootNodes.Add(sitemapFolder);
            }

            // Group 0F: Copilot Studio Bots & Topics
            var copilotNodes = flatNodes.Where(n => n.SubCategory.StartsWith("Copilot", StringComparison.OrdinalIgnoreCase)).ToList();
            if (copilotNodes.Count > 0)
            {
                var copilotFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "🤖 Copilot Studio Bots, Topics, & Knowledge Sources",
                    UniqueKey = "Folder.Copilot"
                };
                foreach (var n in copilotNodes) copilotFolder.Children.Add(n);
                rootNodes.Add(copilotFolder);
            }

            // Group 0G: Connection References & Custom Connectors
            var connRefNodes = flatNodes.Where(n => n.SubCategory.Equals("ConnectionReference", StringComparison.OrdinalIgnoreCase) || 
                                                    n.SubCategory.Equals("CustomConnector", StringComparison.OrdinalIgnoreCase)).ToList();
            if (connRefNodes.Count > 0)
            {
                var connRefFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "🔗 Connection References & Custom Connectors",
                    UniqueKey = "Folder.ConnectionReferences"
                };
                foreach (var n in connRefNodes) connRefFolder.Children.Add(n);
                rootNodes.Add(connRefFolder);
            }

            // Group 0H: Field Security Profiles & Field Permissions
            var fspNodes = flatNodes.Where(n => n.SubCategory.StartsWith("FieldSecurity", StringComparison.OrdinalIgnoreCase) || 
                                                n.SubCategory.StartsWith("FieldPermission", StringComparison.OrdinalIgnoreCase)).ToList();
            if (fspNodes.Count > 0)
            {
                var fspFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "🔐 Field Security Profiles & Field Permissions",
                    UniqueKey = "Folder.FieldSecurity"
                };
                foreach (var n in fspNodes) fspFolder.Children.Add(n);
                rootNodes.Add(fspFolder);
            }

            // Group 1: Entities / Tables Hierarchy (OOB Tables & Custom Tables)
            var entityNodes = flatNodes.Where(n => n.SubCategory.StartsWith("Entity", StringComparison.OrdinalIgnoreCase) || 
                                                  n.SubCategory.Equals("TableColumn", StringComparison.OrdinalIgnoreCase) || 
                                                  n.SubCategory.Equals("OOBTable", StringComparison.OrdinalIgnoreCase) ||
                                                  n.SubCategory.Equals("CustomTable", StringComparison.OrdinalIgnoreCase) ||
                                                  n.SubCategory.Equals("Table", StringComparison.OrdinalIgnoreCase) ||
                                                  n.SubCategory.Equals("BusinessRule", StringComparison.OrdinalIgnoreCase)).ToList();

            if (entityNodes.Count > 0)
            {
                var entitiesFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "📁 Entities / Tables (OOB Tables, Custom Tables, Forms, Views, Columns, Rules)",
                    UniqueKey = "Folder.Entities"
                };

                var groupedByEntity = entityNodes.GroupBy(n => ExtractEntityName(n.UniqueKey));
                foreach (var group in groupedByEntity)
                {
                    var entityFolder = new DiffNode
                    {
                        RootCategory = RootCategory.MetadataCustomizations,
                        SubCategory = "Entity",
                        DisplayName = $"📁 Table: {group.Key}",
                        UniqueKey = $"Entity.{group.Key}"
                    };

                    foreach (var item in group)
                    {
                        entityFolder.Children.Add(item);
                    }

                    entitiesFolder.Children.Add(entityFolder);
                }

                rootNodes.Add(entitiesFolder);
            }

            // Group 2: Plug-in Assemblies, 100% PRT Step Registration Attributes, & Custom APIs
            var pluginNodes = flatNodes.Where(n => n.SubCategory.StartsWith("Plugin", StringComparison.OrdinalIgnoreCase) || 
                                                   n.SubCategory.Equals("CustomAPI", StringComparison.OrdinalIgnoreCase)).ToList();
            if (pluginNodes.Count > 0)
            {
                var pluginsFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "📁 Plug-in Assemblies, Registration Steps (All Config Details), & Custom APIs",
                    UniqueKey = "Folder.Plugins"
                };
                foreach (var n in pluginNodes) pluginsFolder.Children.Add(n);
                rootNodes.Add(pluginsFolder);
            }

            // Group 3: Processes & Automations
            var processNodes = flatNodes.Where(n => n.SubCategory.Contains("Flow", StringComparison.OrdinalIgnoreCase) || 
                                                   n.SubCategory.Contains("Workflow", StringComparison.OrdinalIgnoreCase) || 
                                                   n.SubCategory.Contains("Action", StringComparison.OrdinalIgnoreCase)).ToList();
            if (processNodes.Count > 0)
            {
                var processFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "📁 Processes & Automations (Cloud Flows, Workflows, BPFs)",
                    UniqueKey = "Folder.Processes"
                };
                foreach (var n in processNodes) processFolder.Children.Add(n);
                rootNodes.Add(processFolder);
            }

            // Group 4: Environment Variables (Definitions & Values)
            var envVarNodes = flatNodes.Where(n => n.SubCategory.Equals("EnvVariable", StringComparison.OrdinalIgnoreCase)).ToList();
            if (envVarNodes.Count > 0)
            {
                var envVarFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "🌐 Environment Variables (Definitions & Values)",
                    UniqueKey = "Folder.EnvVariables"
                };
                foreach (var n in envVarNodes) envVarFolder.Children.Add(n);
                rootNodes.Add(envVarFolder);
            }

            // Remaining components
            var handledKeys = new HashSet<string>(solNodes.Concat(appNodes).Concat(dashNodes).Concat(pcfNodes).Concat(sitemapNodes).Concat(copilotNodes).Concat(connRefNodes).Concat(fspNodes).Concat(entityNodes).Concat(pluginNodes).Concat(processNodes).Concat(envVarNodes).Select(n => n.UniqueKey));
            var otherNodes = flatNodes.Where(n => !handledKeys.Contains(n.UniqueKey)).ToList();
            if (otherNodes.Count > 0)
            {
                var otherFolder = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "Folder",
                    DisplayName = "📁 Other Solution Components (Web Resources, Roles, Privileges)",
                    UniqueKey = "Folder.Other"
                };
                foreach (var n in otherNodes) otherFolder.Children.Add(n);
                rootNodes.Add(otherFolder);
            }

            return rootNodes;
        }

        private DiffNode BuildComponentSubTreeForSolution(DiffNode solutionNode, List<DiffNode> flatNodes)
        {
            var solTree = new DiffNode
            {
                RootCategory = solutionNode.RootCategory,
                SubCategory = solutionNode.SubCategory,
                UniqueKey = solutionNode.UniqueKey,
                DisplayName = $"📦 Solution: {solutionNode.DisplayName}",
                Status = solutionNode.Status,
                EnvironmentValues = solutionNode.EnvironmentValues,
                PropertyDiffs = solutionNode.PropertyDiffs
            };

            // Add Components Sub-Folder under this Solution
            var compFolder = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Folder",
                DisplayName = "📁 Solution Components Inventory",
                UniqueKey = $"{solutionNode.UniqueKey}.Components"
            };

            // Populate Solution Component Folders (Tables, Plug-ins, Apps, Flows, Env Variables)
            var tableFolder = new DiffNode { DisplayName = "📁 Entities / Tables", UniqueKey = $"{solutionNode.UniqueKey}.Tables" };
            foreach (var t in flatNodes.Where(n => n.SubCategory.Contains("Table") || n.SubCategory.Contains("Form") || n.SubCategory.Contains("View")))
            {
                tableFolder.Children.Add(t);
            }
            if (tableFolder.Children.Count > 0) compFolder.Children.Add(tableFolder);

            var pluginFolder = new DiffNode { DisplayName = "🧩 Plug-ins & Custom APIs", UniqueKey = $"{solutionNode.UniqueKey}.Plugins" };
            foreach (var p in flatNodes.Where(n => n.SubCategory.Contains("Plugin") || n.SubCategory.Equals("CustomAPI")))
            {
                pluginFolder.Children.Add(p);
            }
            if (pluginFolder.Children.Count > 0) compFolder.Children.Add(pluginFolder);

            var appFolder = new DiffNode { DisplayName = "📱 Apps & Canvas Pages", UniqueKey = $"{solutionNode.UniqueKey}.Apps" };
            foreach (var a in flatNodes.Where(n => n.SubCategory.Contains("App") || n.SubCategory.Contains("Page")))
            {
                appFolder.Children.Add(a);
            }
            if (appFolder.Children.Count > 0) compFolder.Children.Add(appFolder);

            solTree.Children.Add(compFolder);
            return solTree;
        }

        private string ExtractEntityName(string uniqueKey)
        {
            var parts = uniqueKey.Split('.');
            return parts.Length >= 2 ? parts[1] : "General";
        }

        private DiffNode BuildNode(
            string fullKey, 
            RootCategory rootCategory, 
            List<RawEnvData> envDataList, 
            Func<RawEnvData, Dictionary<string, string>?> dictSelector)
        {
            string subCategory = fullKey.Contains('.') ? fullKey.Split('.')[0] : "General";
            string displayName = fullKey.Contains('.') ? fullKey.Substring(fullKey.IndexOf('.') + 1) : fullKey;

            var node = new DiffNode
            {
                RootCategory = rootCategory,
                SubCategory = subCategory,
                UniqueKey = fullKey,
                DisplayName = displayName
            };

            int presentCount = 0;
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var envSummaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var env in envDataList)
            {
                var props = dictSelector(env);
                if (props != null && props.Count > 0)
                {
                    presentCount++;
                    string summaryVal = props.TryGetValue("Value", out var v) ? v : 
                        (props.TryGetValue("Version", out var ver) ? ver : 
                        (props.TryGetValue("Status", out var st) ? st : 
                        (props.TryGetValue("Format", out var fmt) ? fmt : 
                        (props.TryGetValue("FormType", out var ft) ? ft : 
                        (props.TryGetValue("QueryType", out var qt) ? qt : 
                        (props.TryGetValue("MaxLength", out var ml) ? $"MaxLength={ml}" : "Present"))))));

                    envSummaries[env.EnvironmentName] = summaryVal;

                    foreach (var pKey in props.Keys) propertyNames.Add(pKey);
                }
                else
                {
                    envSummaries[env.EnvironmentName] = "Missing 🔷";
                }
            }

            node.EnvironmentValues = envSummaries;

            if (presentCount < envDataList.Count)
            {
                node.Status = DiffStatus.Unique;
            }
            else
            {
                string firstVal = envSummaries.Values.First();
                bool allMatch = envSummaries.Values.All(val => string.Equals(val, firstVal, StringComparison.OrdinalIgnoreCase));
                node.Status = allMatch ? DiffStatus.Identical : DiffStatus.Delta;
            }

            foreach (var propName in propertyNames.OrderBy(p => p))
            {
                var pDiff = new PropertyDiff { PropertyName = propName };
                var vals = new List<string>();

                foreach (var env in envDataList)
                {
                    var props = dictSelector(env);
                    string valStr = (props != null && props.TryGetValue(propName, out var pVal)) ? pVal : "[N/A]";
                    pDiff.ValuesPerEnv[env.EnvironmentName] = valStr;
                    vals.Add(valStr);
                }

                pDiff.IsMismatch = vals.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                node.PropertyDiffs.Add(pDiff);

                if (pDiff.IsMismatch && node.Status == DiffStatus.Identical)
                {
                    node.Status = DiffStatus.Delta;
                }
            }

            return node;
        }

        private bool MatchesFilter(DiffNode node, ComparisonScope scope)
        {
            if (!string.IsNullOrEmpty(scope.SearchFilter))
            {
                bool keyMatch = node.UniqueKey.Contains(scope.SearchFilter, StringComparison.OrdinalIgnoreCase);
                bool nameMatch = node.DisplayName.Contains(scope.SearchFilter, StringComparison.OrdinalIgnoreCase);
                if (!keyMatch && !nameMatch) return false;
            }

            return true;
        }

        private void IncrementStats(ComparisonResult result, DiffStatus status)
        {
            switch (status)
            {
                case DiffStatus.Identical: result.IdenticalCount++; break;
                case DiffStatus.Delta: result.DeltaCount++; break;
                case DiffStatus.Unique: result.UniqueCount++; break;
            }
        }
    }
}
