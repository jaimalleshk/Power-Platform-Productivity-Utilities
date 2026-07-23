using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Utilities.EnvironmentComparator.Models;

namespace Utilities.EnvironmentComparator.Storage
{
    public class ExcelReportGenerator
    {
        /// <summary>
        /// Generates a formatted multi-worksheet Excel Spreadsheet 2003 / XLSX-compatible workbook.
        /// Supports dozens of dedicated worksheets based on selected component categories and entity tables.
        /// </summary>
        public void ExportFormattedExcelXml(string path, ComparisonResult result)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));

            string xml = GenerateExcelMultiWorksheetXml(result);
            File.WriteAllText(path, xml, Encoding.UTF8);
        }

        private string GenerateExcelMultiWorksheetXml(ComparisonResult result)
        {
            var envs = result.TargetEnvironmentNames;
            var envHeaderCells = string.Join("", envs.Select(e => $"<Cell ss:StyleID=\"HeaderStyle\"><Data ss:Type=\"String\">{EscapeXml(e)}</Data></Cell>"));

            var sbWorksheets = new StringBuilder();

            // 1. Sheet 1: Executive Summary
            sbWorksheets.AppendLine(GenerateExecutiveSummarySheet(result, envs));

            // 2. Sheet 2: Admin & OrgDbOrgSettings
            if (result.AdminSettingsNodes.Count > 0)
            {
                sbWorksheets.AppendLine(GenerateCategoryWorksheet("Admin Settings", result.AdminSettingsNodes, envs, envHeaderCells));
            }

            // 3. Flatten Metadata Nodes by Solution Explorer Folders/Categories
            var topFolders = result.MetadataNodes;

            foreach (var topFolder in topFolders)
            {
                if (topFolder.Children == null || topFolder.Children.Count == 0) continue;

                if (topFolder.UniqueKey.Equals("Folder.Entities", StringComparison.OrdinalIgnoreCase))
                {
                    // Generate a dedicated worksheet per Entity / Table! (e.g. Table - Account, Table - Contact)
                    foreach (var entityNode in topFolder.Children)
                    {
                        string sheetName = SanitizeWorksheetName(entityNode.DisplayName.Replace("📁 Table: ", "Table - "));
                        sbWorksheets.AppendLine(GenerateCategoryWorksheet(sheetName, entityNode.Children, envs, envHeaderCells));
                    }
                }
                else
                {
                    string sheetName = SanitizeWorksheetName(topFolder.DisplayName.Replace("📁 ", "").Replace("🤖 ", "").Replace("🔗 ", "").Replace("🔐 ", ""));
                    sbWorksheets.AppendLine(GenerateCategoryWorksheet(sheetName, topFolder.Children, envs, envHeaderCells));
                }
            }

            return $@"<?xml version=""1.0""?>
<?mso-application progid=""Excel.Sheet""?>
<Workbook xmlns=""urn:schemas-microsoft-com:office:spreadsheet""
 xmlns:o=""urn:schemas-microsoft-com:office:office""
 xmlns:x=""urn:schemas-microsoft-com:office:excel""
 xmlns:ss=""urn:schemas-microsoft-com:office:spreadsheet"">
 <Styles>
  <Style ss:ID=""Default"" ss:Name=""Normal"">
   <Alignment ss:Vertical=""Bottom""/>
   <Font ss:FontName=""Segoe UI"" ss:Size=""10"" ss:Color=""#1E293B""/>
  </Style>
  <Style ss:ID=""TitleStyle"">
   <Font ss:FontName=""Segoe UI"" ss:Size=""16"" ss:Bold=""1"" ss:Color=""#0284C7""/>
  </Style>
  <Style ss:ID=""HeaderStyle"">
   <Font ss:FontName=""Segoe UI"" ss:Size=""11"" ss:Bold=""1"" ss:Color=""#FFFFFF""/>
   <Interior ss:Color=""#1E293B"" ss:Pattern=""Solid""/>
   <Alignment ss:Horizontal=""Center"" ss:Vertical=""Center""/>
  </Style>

  <Style ss:ID=""SubCatStyle"">
   <Font ss:FontName=""Segoe UI"" ss:Size=""10"" ss:Color=""#64748B""/>
   <Interior ss:Color=""#F1F5F9"" ss:Pattern=""Solid""/>
  </Style>
  <Style ss:ID=""BoldDataStyle"">
   <Font ss:FontName=""Segoe UI"" ss:Size=""10"" ss:Bold=""1""/>
  </Style>
  <Style ss:ID=""DataStyle"">
   <Font ss:FontName=""Segoe UI"" ss:Size=""10""/>
  </Style>

  <Style ss:ID=""StatusIdentical"">
   <Font ss:FontName=""Segoe UI"" ss:Size=""10"" ss:Bold=""1"" ss:Color=""#166534""/>
   <Interior ss:Color=""#DCFCE7"" ss:Pattern=""Solid""/>
   <Alignment ss:Horizontal=""Center""/>
  </Style>
  <Style ss:ID=""StatusDelta"">
   <Font ss:FontName=""Segoe UI"" ss:Size=""10"" ss:Bold=""1"" ss:Color=""#9A3412""/>
   <Interior ss:Color=""#FFEDD5"" ss:Pattern=""Solid""/>
   <Alignment ss:Horizontal=""Center""/>
  </Style>
  <Style ss:ID=""StatusUnique"">
   <Font ss:FontName=""Segoe UI"" ss:Size=""10"" ss:Bold=""1"" ss:Color=""#075985""/>
   <Interior ss:Color=""#E0F2FE"" ss:Pattern=""Solid""/>
   <Alignment ss:Horizontal=""Center""/>
  </Style>
 </Styles>

{sbWorksheets}
</Workbook>";
        }

        private string GenerateExecutiveSummarySheet(ComparisonResult result, List<string> envs)
        {
            return $@" <Worksheet ss:Name=""Executive Summary"">
  <Table>
   <Column ss:Width=""220""/>
   <Column ss:Width=""250""/>
   <Row><Cell ss:StyleID=""TitleStyle""><Data ss:Type=""String"">Power Platform N-Way Environment Comparison Master Report</Data></Cell></Row>
   <Row><Cell><Data ss:Type=""String"">Generated Date (UTC):</Data></Cell><Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""String"">{result.ComparedAt:yyyy-MM-dd HH:mm:ss}</Data></Cell></Row>
   <Row><Cell><Data ss:Type=""String"">Target Environments:</Data></Cell><Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""String"">{string.Join(", ", envs)}</Data></Cell></Row>
   <Row></Row>
   <Row><Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Metric</Data></Cell><Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Count</Data></Cell></Row>
   <Row><Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""String"">Total Components Compared</Data></Cell><Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""Number"">{result.TotalCount}</Data></Cell></Row>
   <Row><Cell ss:StyleID=""StatusIdentical""><Data ss:Type=""String"">Identical Components 🟢</Data></Cell><Cell ss:StyleID=""StatusIdentical""><Data ss:Type=""Number"">{result.IdenticalCount}</Data></Cell></Row>
   <Row><Cell ss:StyleID=""StatusDelta""><Data ss:Type=""String"">Delta Components ⚠️</Data></Cell><Cell ss:StyleID=""StatusDelta""><Data ss:Type=""Number"">{result.DeltaCount}</Data></Cell></Row>
   <Row><Cell ss:StyleID=""StatusUnique""><Data ss:Type=""String"">Unique Components 🔷</Data></Cell><Cell ss:StyleID=""StatusUnique""><Data ss:Type=""Number"">{result.UniqueCount}</Data></Cell></Row>
  </Table>
 </Worksheet>";
        }

        private string GenerateCategoryWorksheet(string sheetName, IEnumerable<DiffNode> nodes, List<string> envs, string envHeaderCells)
        {
            var sbRows = new StringBuilder();
            WriteNodeRowsRecursive(nodes, envs, sbRows, 0);

            return $@" <Worksheet ss:Name=""{EscapeXml(sheetName)}"">
  <Table>
   <Column ss:Width=""160""/>
   <Column ss:Width=""280""/>
   <Column ss:Width=""110""/>
   {string.Join("", envs.Select(_ => "<Column ss:Width=\"240\"/>"))}
   <Row>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Category</Data></Cell>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Component Name</Data></Cell>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Status</Data></Cell>
    {envHeaderCells}
   </Row>
   {sbRows}
  </Table>
 </Worksheet>";
        }

        private void WriteNodeRowsRecursive(IEnumerable<DiffNode> nodes, List<string> envs, StringBuilder sb, int depth)
        {
            foreach (var node in nodes)
            {
                string statusStyle = node.Status switch
                {
                    DiffStatus.Identical => "StatusIdentical",
                    DiffStatus.Delta => "StatusDelta",
                    _ => "StatusUnique"
                };

                string indent = new string(' ', depth * 2);
                string envValueCells = string.Join("", envs.Select(e => {
                    string val = node.EnvironmentValues.TryGetValue(e, out var v) ? v : "Missing";
                    return $"<Cell ss:StyleID=\"DataStyle\"><Data ss:Type=\"String\">{EscapeXml(val)}</Data></Cell>";
                }));

                sb.AppendLine($@"
                    <Row>
                        <Cell ss:StyleID=""SubCatStyle""><Data ss:Type=""String"">{EscapeXml(node.SubCategory)}</Data></Cell>
                        <Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""String"">{EscapeXml(indent + node.DisplayName)}</Data></Cell>
                        <Cell ss:StyleID=""{statusStyle}""><Data ss:Type=""String"">{node.Status}</Data></Cell>
                        {envValueCells}
                    </Row>");

                if (node.Children != null && node.Children.Count > 0)
                {
                    WriteNodeRowsRecursive(node.Children, envs, sb, depth + 1);
                }
            }
        }

        private static string SanitizeWorksheetName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "Category";
            string name = input.Replace(":", "-").Replace("\\", "").Replace("/", "").Replace("?", "").Replace("*", "").Replace("[", "").Replace("]", "");
            return name.Length > 31 ? name.Substring(0, 31) : name;
        }

        private static string EscapeXml(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("&", "&amp;")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;")
                        .Replace("\"", "&quot;")
                        .Replace("'", "&apos;");
        }
    }
}
