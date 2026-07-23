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
        /// Generates a formatted Excel XML Spreadsheet 2003 / XLSX-compatible report from comparison results.
        /// Includes color status formatting, bold headers, and auto-adjusted table columns.
        /// </summary>
        public void ExportFormattedExcelXml(string path, ComparisonResult result)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));

            string xml = GenerateExcelXmlContent(result);
            File.WriteAllText(path, xml, Encoding.UTF8);
        }

        private string GenerateExcelXmlContent(ComparisonResult result)
        {
            var envs = result.TargetEnvironmentNames;
            var envHeaderCells = string.Join("", envs.Select(e => $"<Cell ss:StyleID=\"HeaderStyle\"><Data ss:Type=\"String\">{EscapeXml(e)}</Data></Cell>"));

            var sbAdminRows = new StringBuilder();
            foreach (var node in result.AdminSettingsNodes)
            {
                string statusStyle = node.Status switch
                {
                    DiffStatus.Identical => "StatusIdentical",
                    DiffStatus.Delta => "StatusDelta",
                    _ => "StatusUnique"
                };

                string envValueCells = string.Join("", envs.Select(e => {
                    string val = node.EnvironmentValues.TryGetValue(e, out var v) ? v : "Missing";
                    return $"<Cell ss:StyleID=\"DataStyle\"><Data ss:Type=\"String\">{EscapeXml(val)}</Data></Cell>";
                }));

                sbAdminRows.AppendLine($@"
                    <Row>
                        <Cell ss:StyleID=""SubCatStyle""><Data ss:Type=""String"">{EscapeXml(node.SubCategory)}</Data></Cell>
                        <Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""String"">{EscapeXml(node.DisplayName)}</Data></Cell>
                        <Cell ss:StyleID=""{statusStyle}""><Data ss:Type=""String"">{node.Status}</Data></Cell>
                        {envValueCells}
                    </Row>");
            }

            var sbMetadataRows = new StringBuilder();
            WriteNodeRowsRecursive(result.MetadataNodes, envs, sbMetadataRows, 0);

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

 <Worksheet ss:Name=""Executive Summary"">
  <Table>
   <Column ss:Width=""180""/>
   <Column ss:Width=""250""/>
   <Row><Cell ss:StyleID=""TitleStyle""><Data ss:Type=""String"">Power Platform N-Way Environment Comparison Report</Data></Cell></Row>
   <Row><Cell><Data ss:Type=""String"">Generated Date (UTC):</Data></Cell><Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""String"">{result.ComparedAt:yyyy-MM-dd HH:mm:ss}</Data></Cell></Row>
   <Row><Cell><Data ss:Type=""String"">Target Environments:</Data></Cell><Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""String"">{string.Join(", ", envs)}</Data></Cell></Row>
   <Row></Row>
   <Row><Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Metric</Data></Cell><Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Count</Data></Cell></Row>
   <Row><Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""String"">Total Components Compared</Data></Cell><Cell ss:StyleID=""BoldDataStyle""><Data ss:Type=""Number"">{result.TotalCount}</Data></Cell></Row>
   <Row><Cell ss:StyleID=""StatusIdentical""><Data ss:Type=""String"">Identical Components 🟢</Data></Cell><Cell ss:StyleID=""StatusIdentical""><Data ss:Type=""Number"">{result.IdenticalCount}</Data></Cell></Row>
   <Row><Cell ss:StyleID=""StatusDelta""><Data ss:Type=""String"">Delta Components ⚠️</Data></Cell><Cell ss:StyleID=""StatusDelta""><Data ss:Type=""Number"">{result.DeltaCount}</Data></Cell></Row>
   <Row><Cell ss:StyleID=""StatusUnique""><Data ss:Type=""String"">Unique Components 🔷</Data></Cell><Cell ss:StyleID=""StatusUnique""><Data ss:Type=""Number"">{result.UniqueCount}</Data></Cell></Row>
  </Table>
 </Worksheet>

 <Worksheet ss:Name=""Root 1 - Admin Settings"">
  <Table>
   <Column ss:Width=""160""/>
   <Column ss:Width=""240""/>
   <Column ss:Width=""110""/>
   {string.Join("", envs.Select(_ => "<Column ss:Width=\"220\"/>"))}
   <Row>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Category</Data></Cell>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Setting Name</Data></Cell>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Status</Data></Cell>
    {envHeaderCells}
   </Row>
   {sbAdminRows}
  </Table>
 </Worksheet>

 <Worksheet ss:Name=""Root 2 - Solution Explorer"">
  <Table>
   <Column ss:Width=""160""/>
   <Column ss:Width=""280""/>
   <Column ss:Width=""110""/>
   {string.Join("", envs.Select(_ => "<Column ss:Width=\"220\"/>"))}
   <Row>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Category</Data></Cell>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Component Name</Data></Cell>
    <Cell ss:StyleID=""HeaderStyle""><Data ss:Type=""String"">Status</Data></Cell>
    {envHeaderCells}
   </Row>
   {sbMetadataRows}
  </Table>
 </Worksheet>
</Workbook>";
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
