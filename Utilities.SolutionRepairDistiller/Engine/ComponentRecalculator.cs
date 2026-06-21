using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Utilities.SolutionRepairDistiller.Models;

namespace Utilities.SolutionRepairDistiller.Engine
{
    public class ComponentRecalculator
    {
        public List<PruningTarget> IdentifyBloat(byte[] zipBytes, out XDocument solutionDoc, out XDocument customizationsDoc)
        {
            solutionDoc = null;
            customizationsDoc = null;
            var pruningTargets = new List<PruningTarget>();

            using (var ms = new MemoryStream(zipBytes))
            {
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    var solutionEntry = archive.GetEntry("solution.xml");
                    if (solutionEntry != null)
                    {
                        using (var s = solutionEntry.Open())
                        {
                            solutionDoc = XDocument.Load(s);
                        }
                    }

                    var customizationsEntry = archive.GetEntry("customizations.xml");
                    if (customizationsEntry != null)
                    {
                        using (var s = customizationsEntry.Open())
                        {
                            customizationsDoc = XDocument.Load(s);
                        }
                    }
                }
            }

            if (customizationsDoc == null) return pruningTargets;

            // Scan for bloated entities (IncludeAllComponents="true" on OOB entities)
            var entities = customizationsDoc.XPathSelectElements("/ImportExportXml/Entities/Entity");
            foreach (var entity in entities)
            {
                string name = entity.Attribute("Name")?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;

                // Check if it is OOB (no publisher prefix underscore) and has IncludeAllComponents="true"
                bool isOob = !name.Contains("_");
                string includeAllStr = entity.Attribute("IncludeAllComponents")?.Value ?? "false";
                bool includeAll = includeAllStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                if (isOob && includeAll)
                {
                    // Identify unmodified system attributes inside this entity
                    var attributes = entity.XPathSelectElements("EntityInfo/Attributes/Attribute");
                    foreach (var attr in attributes)
                    {
                        string attrName = attr.Attribute("PhysicalName")?.Value ?? string.Empty;
                        if (string.IsNullOrEmpty(attrName)) continue;

                        // An attribute is unmodified if:
                        // 1. It is a standard system attribute (no publisher prefix underscore)
                        // 2. It doesn't contain custom node overrides (e.g. customized OptionSet, DisplayName customizations)
                        bool isAttrCustom = attrName.Contains("_");
                        
                        if (!isAttrCustom)
                        {
                            pruningTargets.Add(new PruningTarget
                            {
                                ComponentType = "Attribute",
                                ComponentTypeCode = 2,
                                LogicalName = attrName,
                                ParentTable = name,
                                Reason = "Unmodified system metadata attribute removed by distiller optimization rules."
                            });
                        }
                    }
                }
            }

            return pruningTargets;
        }
    }
}
