using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DataFromPdfActivities.Runner
{
    // Deliberately uses only the line text/x/y retained in the JSON response.
    // This is a conservative layout fallback, not a name/address classifier.
    internal static class UnlabeledColumnRecovery
    {
        internal sealed class Line
        {
            internal Line(string text, double left, double top)
            {
                Text = Normalize(text);
                Left = left;
                Top = top;
            }
            internal string Text { get; }
            internal double Left { get; }
            internal double Top { get; }
        }

        internal static void AddMissingBlocks(Dictionary<string, string> fields, int page, IReadOnlyList<Line> lines)
        {
            var knownLabels = fields.Keys.GroupBy(Identifier, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Count; i++)
            {
                Line previous = lines[i - 1];
                Line first = lines[i];
                // A nearby labelled row entirely to the right is evidence for a separate left column.
                // Never infer the position of a label within a flattened mixed-column row.
                if (Prefix(previous.Text, knownLabels, out string anchor) != string.Empty || string.IsNullOrEmpty(anchor) ||
                    previous.Left - first.Left <= 45 || !Nearby(previous, first))
                    continue;

                var block = new List<string>();
                int end = i;
                for (; end < lines.Count; end++)
                {
                    Line candidate = lines[end];
                    if (Math.Abs(candidate.Left - first.Left) > 24 ||
                        (end > i && !Nearby(lines[end - 1], candidate)))
                        break;

                    string prefix = Prefix(candidate.Text, knownLabels, out _);
                    if (string.IsNullOrWhiteSpace(prefix) || Regex.IsMatch(prefix, @"^[-:;,.\s]+$"))
                        break;
                    block.Add(prefix);
                }

                // Avoid isolated headings and blocks already represented by legacy extraction.
                if (block.Count < 2 || block.All(value => IsRepresented(fields, value)))
                    continue;

                string baseKey = "unlabeledAfter" + char.ToUpperInvariant(anchor[0]) + anchor.Substring(1);
                string key = baseKey;
                int index = 2;
                // Reserve the whole field family, including blank/partial legacy blocks.
                // A collision gets a separate anchored block, never a mix of old/new lines.
                while (fields.Keys.Any(existing => existing.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    existing.StartsWith(key + "Line", StringComparison.OrdinalIgnoreCase) ||
                    existing.Equals(key + "Page", StringComparison.OrdinalIgnoreCase)))
                {
                    key = baseKey + "Block" + (index++).ToString(CultureInfo.InvariantCulture);
                }

                fields.Add(key, string.Join(" ", block));
                fields.Add(key + "Page", page.ToString(CultureInfo.InvariantCulture));
                for (int n = 0; n < block.Count; n++)
                    fields.Add(key + "Line" + (n + 1).ToString(CultureInfo.InvariantCulture), block[n]);
                i = end - 1;
            }
        }

        private static bool Nearby(Line previous, Line current)
        {
            double gap = previous.Top - current.Top;
            return gap > 0 && gap <= 22;
        }

        private static bool IsRepresented(Dictionary<string, string> fields, string text)
        {
            string padded = " " + Normalize(text) + " ";
            return fields.Values.Any(value => (" " + Normalize(value) + " ").IndexOf(padded, StringComparison.Ordinal) >= 0);
        }

        // Resolve the longest known label ending at the first colon. An unknown colon
        // is a boundary: do not guess whether it belongs to prose or a new field.
        private static string Prefix(string text, Dictionary<string, string> knownLabels, out string anchor)
        {
            anchor = null;
            int colon = text.IndexOf(':');
            if (colon < 0) return text;
            for (int start = 0; start < colon; start++)
            {
                if (start > 0 && !char.IsWhiteSpace(text[start - 1])) continue;
                string label = Identifier(text.Substring(start, colon - start));
                if (label.Length > 0 && knownLabels.TryGetValue(label, out anchor))
                    return text.Substring(0, start).Trim();
            }
            return null;
        }

        private static string Identifier(string value) => Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]", "");
        private static string Normalize(string value) => Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
    }
}
