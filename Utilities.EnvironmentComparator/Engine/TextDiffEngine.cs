using System;
using System.Collections.Generic;
using System.Linq;

namespace Utilities.EnvironmentComparator.Engine
{
    public enum LineDiffType
    {
        Unchanged,
        Modified,
        Added,
        Deleted
    }

    public class LineDiffItem
    {
        public int LineNumberA { get; set; }
        public int LineNumberB { get; set; }
        public string ContentA { get; set; } = string.Empty;
        public string ContentB { get; set; } = string.Empty;
        public LineDiffType Type { get; set; } = LineDiffType.Unchanged;
    }

    public class TextDiffEngine
    {
        public List<LineDiffItem> CompareText(string textA, string textB)
        {
            var linesA = (textA ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var linesB = (textB ?? string.Empty).Replace("\r\n", "\n").Split('\n');

            var result = new List<LineDiffItem>();
            int maxLen = Math.Max(linesA.Length, linesB.Length);

            for (int i = 0; i < maxLen; i++)
            {
                string lineA = i < linesA.Length ? linesA[i] : string.Empty;
                string lineB = i < linesB.Length ? linesB[i] : string.Empty;

                if (i >= linesA.Length)
                {
                    result.Add(new LineDiffItem { LineNumberA = 0, LineNumberB = i + 1, ContentA = "", ContentB = lineB, Type = LineDiffType.Added });
                }
                else if (i >= linesB.Length)
                {
                    result.Add(new LineDiffItem { LineNumberA = i + 1, LineNumberB = 0, ContentA = lineA, ContentB = "", Type = LineDiffType.Deleted });
                }
                else if (string.Equals(lineA, lineB, StringComparison.Ordinal))
                {
                    result.Add(new LineDiffItem { LineNumberA = i + 1, LineNumberB = i + 1, ContentA = lineA, ContentB = lineB, Type = LineDiffType.Unchanged });
                }
                else
                {
                    result.Add(new LineDiffItem { LineNumberA = i + 1, LineNumberB = i + 1, ContentA = lineA, ContentB = lineB, Type = LineDiffType.Modified });
                }
            }

            return result;
        }
    }
}
