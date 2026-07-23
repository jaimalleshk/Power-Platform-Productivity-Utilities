using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Engine
{
    public static class SolutionExplorerTreeBuilder
    {
        public static ObservableCollection<DiffNode> BuildDefaultSkeletonTree(bool filterCustomOnly = false)
        {
            var tree = new ObservableCollection<DiffNode>();

            // ==========================================
            // ROOT 1: ADMIN & ENVIRONMENT SETTINGS
            // ==========================================
            var root1 = new DiffNode
            {
                RootCategory = RootCategory.AdminSettings,
                SubCategory = "Folder",
                DisplayName = "⚙️ Root 1: Admin & Environment Settings",
                UniqueKey = "Root1.AdminSettings",
                IsExpanded = true,
                IsChecked = true
            };

            root1.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.AdminSettings,
                SubCategory = "OrgDbSettings",
                DisplayName = "⚙️ Organization Database Settings (OrgDbOrgSettings)",
                UniqueKey = "Root1.OrgDbSettings",
                IsChecked = true
            });
            root1.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.AdminSettings,
                SubCategory = "EnvVars",
                DisplayName = "🔑 Environment Variables & Azure KeyVault Secrets",
                UniqueKey = "Root1.EnvVars",
                IsChecked = true
            });
            root1.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.AdminSettings,
                SubCategory = "FeatureFlags",
                DisplayName = "🛡️ Tenant Feature Flags & Security Governance",
                UniqueKey = "Root1.FeatureFlags",
                IsChecked = true
            });
            root1.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.AdminSettings,
                SubCategory = "SystemSettings",
                DisplayName = "⚙️ Dataverse System & Email Settings",
                UniqueKey = "Root1.SystemSettings",
                IsChecked = true
            });

            // ==========================================
            // ROOT 2: SOLUTION EXPLORER & CUSTOMIZATIONS
            // ==========================================
            var root2 = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Folder",
                DisplayName = "📦 Root 2: Solution Explorer & Customizations",
                UniqueKey = "Root2.SolutionExplorer",
                IsExpanded = true,
                IsChecked = true
            };

            // 1. Standard Solution Explorer Folder (15 Core Components)
            var stdFolder = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Folder",
                DisplayName = "📁 Standard Solution Explorer (15 Core Component Types)",
                UniqueKey = "Root2.StandardExplorer",
                IsExpanded = true,
                IsChecked = true
            };

            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Tables",
                DisplayName = "📊 Dataverse Tables & Schema Customizations (Entities/Columns)",
                UniqueKey = "Root2.Tables",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "OptionSets",
                DisplayName = "🔠 Global Option Sets / Choice Columns",
                UniqueKey = "Root2.OptionSets",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "WebResources",
                DisplayName = "🌐 Web Resources (JavaScript, HTML, CSS, SVG, XML)",
                UniqueKey = "Root2.WebResources",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Workflows",
                DisplayName = "⚡ Processes (Workflows, Cloud Flows, Business Process Flows)",
                UniqueKey = "Root2.Workflows",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "PluginAssemblies",
                DisplayName = "🧩 Plug-in Assemblies & Custom APIs",
                UniqueKey = "Root2.Plugins",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "PluginSteps",
                DisplayName = "📩 SDK Message Processing Steps (PRT Registrations)",
                UniqueKey = "Root2.PluginSteps",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "SecurityRoles",
                DisplayName = "🔐 Security Roles & Field Security Profiles",
                UniqueKey = "Root2.SecurityRoles",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Apps",
                DisplayName = "📱 Model-Driven Apps & Canvas Power Apps",
                UniqueKey = "Root2.Apps",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Dashboards",
                DisplayName = "📊 System Dashboards & Saved Queries / Public Views",
                UniqueKey = "Root2.Dashboards",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "SiteMaps",
                DisplayName = "🗺️ Site Maps & Navigation Menus",
                UniqueKey = "Root2.SiteMaps",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.AdminSettings,
                SubCategory = "EnvVars",
                DisplayName = "🔑 Environment Variables & Secrets",
                UniqueKey = "Root2.EnvVars",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Connectors",
                DisplayName = "🔌 Connection References & Custom Connectors",
                UniqueKey = "Root2.Connectors",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Copilots",
                DisplayName = "🤖 Copilot Studio Bots & Component Topics",
                UniqueKey = "Root2.Copilots",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "AiModels",
                DisplayName = "🧠 AI Builder Models & Prompts",
                UniqueKey = "Root2.AiModels",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "PcfControls",
                DisplayName = "📦 PCF Controls & Custom Control Resources",
                UniqueKey = "Root2.PcfControls",
                IsChecked = true
            });

            root2.Children.Add(stdFolder);

            // 2. Solutions Directory (Per-Solution Hierarchy)
            var solDirectoryFolder = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Folder",
                DisplayName = "📦 System Solutions Directory (Packages & Composition)",
                UniqueKey = "Root2.SolutionsDirectory",
                IsExpanded = true,
                IsChecked = true
            };

            var defaultSolNode = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "SolutionPackage",
                DisplayName = "📦 Default Solution (Default / Unmanaged)",
                UniqueKey = "Sol.Default",
                IsChecked = true
            };
            solDirectoryFolder.Children.Add(defaultSolNode);

            var customSolANode = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "SolutionPackage",
                DisplayName = "📦 ContosoCoreCustomizations (Custom / Unmanaged v1.4.0.0)",
                UniqueKey = "Sol.ContosoCoreCustomizations",
                IsChecked = true
            };
            solDirectoryFolder.Children.Add(customSolANode);

            if (!filterCustomOnly)
            {
                var msSolNode = new DiffNode
                {
                    RootCategory = RootCategory.MetadataCustomizations,
                    SubCategory = "SolutionPackage",
                    DisplayName = "📦 msdyn_FieldService (Microsoft First-Party Managed v8.8.40.12)",
                    UniqueKey = "Sol.msdyn_FieldService",
                    IsChecked = true
                };
                solDirectoryFolder.Children.Add(msSolNode);
            }

            root2.Children.Add(solDirectoryFolder);

            tree.Add(root1);
            tree.Add(root2);

            return tree;
        }
    }
}
