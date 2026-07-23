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

            // 1. Standard CRM Solution Explorer Folder
            var stdFolder = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Folder",
                DisplayName = "📁 Standard Solution Explorer (CRM Components)",
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
                SubCategory = "Workflows",
                DisplayName = "⚡ Workflows, Power Automate & Cloud Flows",
                UniqueKey = "Root2.Workflows",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "PluginAssemblies",
                DisplayName = "🧩 Plugin Assemblies, Custom Steps & SDK Messages",
                UniqueKey = "Root2.Plugins",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "SecurityRoles",
                DisplayName = "🔐 Security Roles & Table Privileges",
                UniqueKey = "Root2.SecurityRoles",
                IsChecked = true
            });
            stdFolder.Children.Add(new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "WebResources",
                DisplayName = "🌐 Web Resources (JavaScript, HTML, CSS, SVG)",
                UniqueKey = "Root2.WebResources",
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
                DisplayName = "📊 System Dashboards & Public Views",
                UniqueKey = "Root2.Dashboards",
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

            root2.Children.Add(stdFolder);

            // 2. Solutions Directory (Per-Solution Structure)
            var solDirectoryFolder = new DiffNode
            {
                RootCategory = RootCategory.MetadataCustomizations,
                SubCategory = "Folder",
                DisplayName = "📦 System Solutions Directory (Packages & Composition)",
                UniqueKey = "Root2.SolutionsDirectory",
                IsExpanded = true,
                IsChecked = true
            };

            // Sample / Pre-loaded System Solutions
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
