using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.EnvironmentComparator.Engine
{
    public class DiffLine
    {
        public int LineNumberOld { get; set; }
        public int LineNumberNew { get; set; }
        public string Content { get; set; } = string.Empty;
        public DiffLineType Type { get; set; }
    }

    public enum DiffLineType
    {
        Unchanged,
        Added,
        Deleted,
        Modified
    }

    public class TextDiffEngine
    {
        public List<DiffLine> ComputeDiff(string text1, string text2)
        {
            var lines1 = (text1 ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var lines2 = (text2 ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var diffResult = new List<DiffLine>();
            int max = Math.Max(lines1.Length, lines2.Length);

            for (int i = 0; i < max; i++)
            {
                string? l1 = i < lines1.Length ? lines1[i] : null;
                string? l2 = i < lines2.Length ? lines2[i] : null;

                if (l1 != null && l2 != null)
                {
                    if (string.Equals(l1, l2, StringComparison.Ordinal))
                    {
                        diffResult.Add(new DiffLine { LineNumberOld = i + 1, LineNumberNew = i + 1, Content = l1, Type = DiffLineType.Unchanged });
                    }
                    else
                    {
                        diffResult.Add(new DiffLine { LineNumberOld = i + 1, LineNumberNew = i + 1, Content = l2, Type = DiffLineType.Modified });
                    }
                }
                else if (l1 == null && l2 != null)
                {
                    diffResult.Add(new DiffLine { LineNumberOld = 0, LineNumberNew = i + 1, Content = l2, Type = DiffLineType.Added });
                }
                else if (l1 != null && l2 == null)
                {
                    diffResult.Add(new DiffLine { LineNumberOld = i + 1, LineNumberNew = 0, Content = l1, Type = DiffLineType.Deleted });
                }
            }

            return diffResult;
        }

        public string GenerateHtmlColorDiffView(string oldText, string newText, string fileType = "js")
        {
            var diffLines = ComputeDiff(oldText, newText);
            var sb = new StringBuilder();

            sb.AppendLine(@"<div class=""code-diff-container"" style=""font-family: 'Consolas', 'Fira Code', monospace; font-size: 13px; background: #0f172a; color: #f8fafc; border-radius: 8px; padding: 12px; overflow-x: auto;"">");
            sb.AppendLine(@"<table style=""width: 100%; border-collapse: collapse;"">");

            foreach (var line in diffLines)
            {
                string rowBg = line.Type switch
                {
                    DiffLineType.Added => "rgba(34, 197, 94, 0.15); border-left: 4px solid #22c55e;",
                    DiffLineType.Deleted => "rgba(239, 68, 68, 0.15); border-left: 4px solid #ef4444;",
                    DiffLineType.Modified => "rgba(245, 158, 11, 0.15); border-left: 4px solid #f59e0b;",
                    _ => "transparent;"
                };

                string typeBadge = line.Type switch
                {
                    DiffLineType.Added => "<span style=\"color: #4ade80; font-weight: bold;\">+</span>",
                    DiffLineType.Deleted => "<span style=\"color: #f87171; font-weight: bold;\">-</span>",
                    DiffLineType.Modified => "<span style=\"color: #fbbf24; font-weight: bold;\">~</span>",
                    _ => "&nbsp;"
                };

                string formattedContent = fileType.ToLowerInvariant() switch
                {
                    "js" or "javascript" => FormatJavaScriptSyntax(line.Content),
                    _ => System.Net.WebUtility.HtmlEncode(line.Content)
                };

                sb.AppendLine($"<tr style=\"background: {rowBg}\">");
                sb.AppendLine($"<td style=\"width: 40px; color: #64748b; text-align: right; padding-right: 8px; user-select: none;\">{line.LineNumberOld}</td>");
                sb.AppendLine($"<td style=\"width: 40px; color: #64748b; text-align: right; padding-right: 8px; user-select: none;\">{line.LineNumberNew}</td>");
                sb.AppendLine($"<td style=\"width: 20px; text-align: center;\">{typeBadge}</td>");
                sb.AppendLine($"<td style=\"white-space: pre-wrap; font-family: inherit;\">{formattedContent}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table></div>");
            return sb.ToString();
        }

        public string FormatJavaScriptSyntax(string codeLine)
        {
            if (string.IsNullOrEmpty(codeLine)) return "";

            string encoded = System.Net.WebUtility.HtmlEncode(codeLine);

            // Highlight JS Comments
            if (encoded.TrimStart().StartsWith("//"))
            {
                return $"<span style=\"color: #6a9955; font-style: italic;\">{encoded}</span>";
            }

            // Highlight JS Keywords
            string keywordsPattern = @"\b(function|var|let|const|return|if|else|switch|case|break|for|while|try|catch|async|await|new|this)\b";
            encoded = Regex.Replace(encoded, keywordsPattern, "<span style=\"color: #569cd6; font-weight: bold;\">$1</span>");

            // Highlight Strings
            encoded = Regex.Replace(encoded, "(&quot;.*?&quot;|'.*?')", "<span style=\"color: #ce9178;\">$1</span>");

            // Highlight Numbers
            encoded = Regex.Replace(encoded, @"\b(\d+)\b", "<span style=\"color: #b5cea8;\">$1</span>");

            return encoded;
        }
    }
}
