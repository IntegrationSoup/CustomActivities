using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DataFromPdfActivities.Runner
{
    internal static class Program
    {
        private const string OperationName = "extract-data-from-pdf";

        private static int Main(string[] args)
        {
            try
            {
                int? serverExitCode = PersistentRunnerServer.RunIfRequested(args, HandleServerRequest);
                if (serverExitCode.HasValue)
                {
                    return serverExitCode.Value;
                }

                return RunOneShot(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static int RunOneShot(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: DataFromPdfRunner <inputPdfPath> [outputJsonPath]");
                return 2;
            }

            string inputPdfPath = Path.GetFullPath(args[0]);
            if (!File.Exists(inputPdfPath))
            {
                Console.Error.WriteLine($"Input PDF file was not found: {inputPdfPath}");
                return 3;
            }

            string json = PdfDataExtractor.Extract(File.ReadAllBytes(inputPdfPath));
            if (args.Length >= 2)
            {
                string outputJsonPath = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(outputJsonPath) ?? AppDomain.CurrentDomain.BaseDirectory);
                File.WriteAllText(outputJsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            else
            {
                Console.Out.WriteLine(json);
            }

            return 0;
        }

        private static string HandleServerRequest(string operation, string payloadJson)
        {
            if (!string.Equals(operation, OperationName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported Data from PDF operation '{operation}'.");
            }

            PdfDataPipeRequest request = PersistentRunnerJson.Deserialize<PdfDataPipeRequest>(payloadJson);
            if (request == null || string.IsNullOrWhiteSpace(request.PdfBase64))
            {
                throw new InvalidOperationException("The PDF request payload was empty.");
            }

            PdfDataPipeResponse response = new PdfDataPipeResponse
            {
                Json = PdfDataExtractor.Extract(Convert.FromBase64String(request.PdfBase64))
            };

            return PersistentRunnerJson.Serialize(response);
        }

        [DataContract]
        private sealed class PdfDataPipeRequest
        {
            [DataMember(Order = 1)]
            public string PdfBase64 { get; set; }
        }

        [DataContract]
        private sealed class PdfDataPipeResponse
        {
            [DataMember(Order = 1)]
            public string Json { get; set; }
        }
    }

    internal static class PdfDataExtractor
    {
        private static readonly Regex LabelRegex = new Regex(
            @"(?<label>[A-Za-z][A-Za-z0-9 /&().,#'""-]{0,60}?):",
            RegexOptions.Compiled);

        internal static string Extract(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                throw new InvalidOperationException("The PDF payload was empty.");
            }

            List<PageExtraction> pages = new List<PageExtraction>();
            List<string> warnings = new List<string>();

            using (PdfDocument document = PdfDocument.Open(pdfBytes))
            {
                foreach (Page page in document.GetPages())
                {
                    pages.Add(ExtractPage(page));
                }
            }

            if (pages.Count == 0)
            {
                warnings.Add("The PDF did not contain any pages.");
            }

            if (!pages.Any(page => page.Lines.Count > 0))
            {
                warnings.Add("No extractable text was found. The PDF may be scanned image content.");
            }

            Dictionary<string, string> fields = ExtractFields(pages);
            return PdfJsonWriter.Write(pages, fields, warnings);
        }

        private static PageExtraction ExtractPage(Page page)
        {
            List<LayoutWord> words = page.GetWords()
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new LayoutWord(
                    word.Text,
                    word.BoundingBox.Left,
                    word.BoundingBox.Right,
                    word.BoundingBox.Top,
                    word.BoundingBox.Bottom))
                .ToList();

            List<LayoutLine> lines = GroupWordsIntoLines(words);
            string text = string.Join(Environment.NewLine, lines.Select(line => line.Text));
            return new PageExtraction(page.Number, page.Width, page.Height, text, lines);
        }

        private static List<LayoutLine> GroupWordsIntoLines(IReadOnlyList<LayoutWord> words)
        {
            List<LayoutLine> lines = new List<LayoutLine>();
            foreach (LayoutWord word in words.OrderByDescending(word => word.CenterY).ThenBy(word => word.Left))
            {
                LayoutLine line = lines.FirstOrDefault(candidate => IsSameLine(candidate, word));
                if (line == null)
                {
                    line = new LayoutLine();
                    lines.Add(line);
                }

                line.Add(word);
            }

            return lines
                .OrderByDescending(line => line.CenterY)
                .Select(line => line.Freeze())
                .ToList();
        }

        private static bool IsSameLine(LayoutLine line, LayoutWord word)
        {
            double tolerance = Math.Max(3.0, Math.Max(line.AverageHeight, word.Height) * 0.65);
            return Math.Abs(line.CenterY - word.CenterY) <= tolerance;
        }

        private static Dictionary<string, string> ExtractFields(IReadOnlyList<PageExtraction> pages)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int unlabeledBlockIndex = 1;
            foreach (PageExtraction page in pages)
            {
                for (int lineIndex = 0; lineIndex < page.Lines.Count; lineIndex++)
                {
                    LayoutLine line = page.Lines[lineIndex];
                    List<TextSegment> segments = line.GetSegments().ToList();
                    for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                    {
                        TextSegment nextSegment = segmentIndex + 1 < segments.Count ? segments[segmentIndex + 1] : null;
                        AddFieldsFromSegment(fields, page.Lines, lineIndex, segments[segmentIndex], nextSegment);
                    }
                }

                AddAnchoredUnlabeledBlocks(fields, page, ref unlabeledBlockIndex);
            }

            return fields;
        }

        private static void AddFieldsFromSegment(
            Dictionary<string, string> fields,
            IReadOnlyList<LayoutLine> lines,
            int lineIndex,
            TextSegment segment,
            TextSegment nextSegment)
        {
            string segmentText = segment?.Text ?? string.Empty;
            MatchCollection matches = LabelRegex.Matches(segmentText ?? string.Empty);
            if (matches.Count == 0)
            {
                return;
            }

            for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                Match match = matches[matchIndex];
                string label = CleanLabel(match.Groups["label"].Value);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                int valueStart = match.Index + match.Length;
                int valueEnd = matchIndex + 1 < matches.Count
                    ? matches[matchIndex + 1].Index
                    : segmentText.Length;

                string value = NormalizeWhitespace(segmentText.Substring(valueStart, Math.Max(0, valueEnd - valueStart)));
                if (matchIndex + 1 == matches.Count)
                {
                    if (string.IsNullOrWhiteSpace(value) && nextSegment != null && !LineContainsLabel(nextSegment.Text))
                    {
                        value = nextSegment.Text;
                    }

                    value = AppendContinuation(lines, lineIndex, value, segment.Left);
                }

                string key = ToCamelCase(label);
                if (string.IsNullOrWhiteSpace(value) && !ShouldKeepEmptyField(matchIndex + 1 < matches.Count))
                {
                    continue;
                }

                AddField(fields, key, value);
            }
        }

        private static bool ShouldKeepEmptyField(bool followedByAnotherLabel)
        {
            return !followedByAnotherLabel;
        }

        private static void AddAnchoredUnlabeledBlocks(
            Dictionary<string, string> fields,
            PageExtraction page,
            ref int unlabeledBlockIndex)
        {
            if (page == null || page.Lines.Count == 0)
            {
                return;
            }

            HashSet<string> processedAnchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int lineIndex = 0; lineIndex < page.Lines.Count; lineIndex++)
            {
                LayoutLine line = page.Lines[lineIndex];
                foreach (TextSegment segment in line.GetSegments())
                {
                    foreach (string label in GetBlankSegmentLabels(segment.Text))
                    {
                        string key = ToCamelCase(label);
                        string anchorId = key + "@" + lineIndex.ToString(CultureInfo.InvariantCulture);
                        if (string.IsNullOrWhiteSpace(key) ||
                            processedAnchors.Contains(anchorId) ||
                            fields.TryGetValue(key, out string existingValue) && !string.IsNullOrWhiteSpace(existingValue))
                        {
                            continue;
                        }

                        IReadOnlyList<LayoutLine> unlabeledBlock = FindUnlabeledBlockAfterLine(page, lineIndex);
                        if (unlabeledBlock.Count == 0)
                        {
                            continue;
                        }

                        AddField(fields, key, string.Empty);

                        string anchorKey = "unlabeledAfter" + ToPascalIdentifier(key);
                        AddField(fields, anchorKey, string.Join(" ", unlabeledBlock.Select(blockLine => blockLine.Text)));
                        for (int index = 0; index < unlabeledBlock.Count; index++)
                        {
                            AddField(
                                fields,
                                anchorKey + "Line" + (index + 1).ToString(CultureInfo.InvariantCulture),
                                unlabeledBlock[index].Text);
                        }

                        AddField(fields, "unlabeledBlock" + unlabeledBlockIndex.ToString(CultureInfo.InvariantCulture) + "Page", page.Number.ToString(CultureInfo.InvariantCulture));
                        AddField(fields, "unlabeledBlock" + unlabeledBlockIndex.ToString(CultureInfo.InvariantCulture) + "Anchor", key);
                        AddField(fields, "unlabeledBlock" + unlabeledBlockIndex.ToString(CultureInfo.InvariantCulture), string.Join(" ", unlabeledBlock.Select(blockLine => blockLine.Text)));
                        for (int index = 0; index < unlabeledBlock.Count; index++)
                        {
                            AddField(
                                fields,
                                "unlabeledBlock" + unlabeledBlockIndex.ToString(CultureInfo.InvariantCulture) + "Line" + (index + 1).ToString(CultureInfo.InvariantCulture),
                                unlabeledBlock[index].Text);
                        }

                        unlabeledBlockIndex++;
                        processedAnchors.Add(anchorId);
                    }
                }
            }
        }

        private static IEnumerable<string> GetBlankSegmentLabels(string segmentText)
        {
            string text = segmentText ?? string.Empty;
            MatchCollection matches = LabelRegex.Matches(text);
            for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                Match match = matches[matchIndex];
                string label = CleanLabel(match.Groups["label"].Value);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                int valueStart = match.Index + match.Length;
                int valueEnd = matchIndex + 1 < matches.Count
                    ? matches[matchIndex + 1].Index
                    : text.Length;

                string value = NormalizeWhitespace(text.Substring(valueStart, Math.Max(0, valueEnd - valueStart)));
                if (string.IsNullOrWhiteSpace(value))
                {
                    yield return label;
                }
            }
        }

        private static IReadOnlyList<LayoutLine> FindUnlabeledBlockAfterLine(PageExtraction page, int anchorLineIndex)
        {
            List<LayoutLine> block = new List<LayoutLine>();
            for (int index = anchorLineIndex + 1; index < page.Lines.Count; index++)
            {
                LayoutLine candidate = page.Lines[index];
                string text = NormalizeWhitespace(candidate?.Text);
                if (string.IsNullOrWhiteSpace(text) || IsStandalonePunctuation(text))
                {
                    continue;
                }

                if (LineContainsLabel(text))
                {
                    break;
                }

                block.Add(candidate);
            }

            return block;
        }

        private static string AppendContinuation(IReadOnlyList<LayoutLine> lines, int lineIndex, string currentValue, double anchorLeft)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(currentValue))
            {
                parts.Add(currentValue);
            }

            int maxContinuationLines = string.IsNullOrWhiteSpace(currentValue) ? 1 : 3;
            LayoutLine previousLine = lines[lineIndex];
            for (int index = lineIndex + 1; index < lines.Count && parts.Count <= maxContinuationLines; index++)
            {
                LayoutLine candidateLine = lines[index];
                if (Math.Abs(previousLine.CenterY - candidateLine.CenterY) > 22.0)
                {
                    break;
                }

                if (!IsContinuationColumn(anchorLeft, candidateLine.Left))
                {
                    break;
                }

                string nextText = lines[index].Text;
                if (string.IsNullOrWhiteSpace(nextText) || IsStandalonePunctuation(nextText))
                {
                    continue;
                }

                if (LineContainsLabel(nextText))
                {
                    break;
                }

                parts.Add(nextText);
                previousLine = candidateLine;
            }

            return NormalizeWhitespace(string.Join(" ", parts));
        }

        private static bool IsContinuationColumn(double anchorLeft, double candidateLeft)
        {
            return candidateLeft >= anchorLeft - 24.0;
        }

        private static bool LineContainsLabel(string text)
        {
            return LabelRegex.IsMatch(text ?? string.Empty);
        }

        private static bool IsStandalonePunctuation(string text)
        {
            return Regex.IsMatch(text ?? string.Empty, @"^\s*[-:;,.]+\s*$");
        }

        private static string CleanLabel(string value)
        {
            string normalized = NormalizeWhitespace(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string[] words = normalized.Split(' ');
            if (words.Length > 3)
            {
                normalized = string.Join(" ", words.Skip(words.Length - 3));
            }

            return normalized.Trim(' ', '-', ':');
        }

        private static string ToCamelCase(string label)
        {
            string[] words = Regex.Split(label ?? string.Empty, @"[^A-Za-z0-9]+")
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

            if (words.Length == 0)
            {
                return "field";
            }

            StringBuilder builder = new StringBuilder(ToFirstCamelWord(words[0]));

            foreach (string word in words.Skip(1))
            {
                builder.Append(ToPascalWord(word));
            }

            return builder.ToString();
        }

        private static string ToFirstCamelWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return string.Empty;
            }

            if (word.All(char.IsUpper))
            {
                return word.ToLowerInvariant();
            }

            return char.ToLowerInvariant(word[0]) + (word.Length > 1 ? word.Substring(1) : string.Empty);
        }

        private static string ToPascalWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return string.Empty;
            }

            if (word.All(char.IsUpper))
            {
                word = word.ToLowerInvariant();
            }

            return char.ToUpperInvariant(word[0]) + (word.Length > 1 ? word.Substring(1) : string.Empty);
        }

        private static string ToPascalIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return "Field";
            }

            return char.ToUpperInvariant(identifier[0]) + (identifier.Length > 1 ? identifier.Substring(1) : string.Empty);
        }

        private static string CreateUniqueKey(Dictionary<string, string> fields, string baseKey)
        {
            string key = string.IsNullOrWhiteSpace(baseKey) ? "field" : baseKey;
            if (!fields.ContainsKey(key))
            {
                return key;
            }

            for (int index = 2; ; index++)
            {
                string candidate = key + index.ToString(CultureInfo.InvariantCulture);
                if (!fields.ContainsKey(candidate))
                {
                    return candidate;
                }
            }
        }

        private static void AddField(Dictionary<string, string> fields, string baseKey, string value)
        {
            string key = string.IsNullOrWhiteSpace(baseKey) ? "field" : baseKey;
            string safeValue = NormalizeWhitespace(value);

            if (!fields.TryGetValue(key, out string existingValue))
            {
                fields[key] = safeValue;
                return;
            }

            if (string.IsNullOrWhiteSpace(existingValue) && !string.IsNullOrWhiteSpace(safeValue))
            {
                fields[key] = safeValue;
                return;
            }

            if (string.IsNullOrWhiteSpace(safeValue) || string.Equals(existingValue, safeValue, StringComparison.Ordinal))
            {
                return;
            }

            fields[CreateUniqueKey(fields, key)] = safeValue;
        }

        private static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        internal sealed class PageExtraction
        {
            internal PageExtraction(int number, double width, double height, string text, IReadOnlyList<LayoutLine> lines)
            {
                Number = number;
                Width = width;
                Height = height;
                Text = text ?? string.Empty;
                Lines = lines ?? new List<LayoutLine>();
            }

            internal int Number { get; }
            internal double Width { get; }
            internal double Height { get; }
            internal string Text { get; }
            internal IReadOnlyList<LayoutLine> Lines { get; }
        }

        internal sealed class LayoutWord
        {
            internal LayoutWord(string text, double left, double right, double top, double bottom)
            {
                Text = text ?? string.Empty;
                Left = left;
                Right = right;
                Top = top;
                Bottom = bottom;
            }

            internal string Text { get; }
            internal double Left { get; }
            internal double Right { get; }
            internal double Top { get; }
            internal double Bottom { get; }
            internal double CenterY => (Top + Bottom) / 2.0;
            internal double Height => Math.Abs(Top - Bottom);
        }

        internal sealed class LayoutLine
        {
            private readonly List<LayoutWord> words = new List<LayoutWord>();

            internal double CenterY { get; private set; }
            internal double AverageHeight { get; private set; }
            internal string Text { get; private set; }
            internal double Left { get; private set; }
            internal double Top { get; private set; }
            internal double Width { get; private set; }
            internal double Height { get; private set; }

            internal void Add(LayoutWord word)
            {
                words.Add(word);
                CenterY = words.Average(candidate => candidate.CenterY);
                AverageHeight = words.Average(candidate => candidate.Height);
            }

            internal LayoutLine Freeze()
            {
                List<LayoutWord> orderedWords = words.OrderBy(word => word.Left).ToList();
                Text = NormalizeWhitespace(string.Join(" ", orderedWords.Select(word => word.Text)));

                double right = orderedWords.Max(word => word.Right);
                double bottom = orderedWords.Min(word => word.Bottom);
                Left = orderedWords.Min(word => word.Left);
                Top = orderedWords.Max(word => word.Top);
                Width = right - Left;
                Height = Top - bottom;
                return this;
            }

            internal IEnumerable<TextSegment> GetSegments()
            {
                List<LayoutWord> orderedWords = words.OrderBy(word => word.Left).ToList();
                List<LayoutWord> segment = new List<LayoutWord>();
                LayoutWord previous = null;

                foreach (LayoutWord word in orderedWords)
                {
                    if (previous != null && word.Left - previous.Right > 45.0)
                    {
                        yield return new TextSegment(segment);
                        segment.Clear();
                    }

                    segment.Add(word);
                    previous = word;
                }

                if (segment.Count > 0)
                {
                    yield return new TextSegment(segment);
                }
            }
        }

        internal sealed class TextSegment
        {
            internal TextSegment(IReadOnlyList<LayoutWord> words)
            {
                IReadOnlyList<LayoutWord> safeWords = words ?? Array.Empty<LayoutWord>();
                Text = NormalizeWhitespace(string.Join(" ", safeWords.Select(word => word.Text)));
                Left = safeWords.Count == 0 ? 0 : safeWords.Min(word => word.Left);
            }

            internal string Text { get; }
            internal double Left { get; }
        }
    }

    internal static class PdfJsonWriter
    {
        internal static string Write(
            IReadOnlyList<PdfDataExtractor.PageExtraction> pages,
            IReadOnlyDictionary<string, string> fields,
            IReadOnlyList<string> warnings)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('{');

            AppendDocument(builder, pages);
            builder.Append(',');
            AppendData(builder, fields);
            builder.Append(',');
            AppendText(builder, pages);
            builder.Append(',');
            AppendPages(builder, pages);
            builder.Append(',');
            AppendStringArrayProperty(builder, "warnings", warnings);

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendDocument(StringBuilder builder, IReadOnlyList<PdfDataExtractor.PageExtraction> pages)
        {
            builder.Append("\"document\":{");
            AppendStringProperty(builder, "format", "pdf");
            builder.Append(',');
            AppendNumberProperty(builder, "pageCount", pages.Count);
            builder.Append('}');
        }

        private static void AppendData(StringBuilder builder, IReadOnlyDictionary<string, string> fields)
        {
            builder.Append("\"data\":{");
            builder.Append("\"fields\":{");

            bool first = true;
            foreach (KeyValuePair<string, string> field in fields.OrderBy(field => field.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!first)
                {
                    builder.Append(',');
                }

                AppendStringProperty(builder, field.Key, field.Value);
                first = false;
            }

            builder.Append("}}");
        }

        private static void AppendText(StringBuilder builder, IReadOnlyList<PdfDataExtractor.PageExtraction> pages)
        {
            builder.Append("\"text\":{");
            AppendStringProperty(builder, "full", string.Join(Environment.NewLine + Environment.NewLine, pages.Select(page => page.Text)));
            builder.Append(',');
            AppendStringArrayProperty(builder, "pages", pages.Select(page => page.Text).ToList());
            builder.Append('}');
        }

        private static void AppendPages(StringBuilder builder, IReadOnlyList<PdfDataExtractor.PageExtraction> pages)
        {
            builder.Append("\"pages\":[");
            for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                if (pageIndex > 0)
                {
                    builder.Append(',');
                }

                PdfDataExtractor.PageExtraction page = pages[pageIndex];
                builder.Append('{');
                AppendNumberProperty(builder, "number", page.Number);
                builder.Append(',');
                AppendDoubleProperty(builder, "width", page.Width);
                builder.Append(',');
                AppendDoubleProperty(builder, "height", page.Height);
                builder.Append(',');
                AppendStringProperty(builder, "text", page.Text);
                builder.Append(',');
                AppendLines(builder, page.Lines);
                builder.Append('}');
            }

            builder.Append(']');
        }

        private static void AppendLines(StringBuilder builder, IReadOnlyList<PdfDataExtractor.LayoutLine> lines)
        {
            builder.Append("\"lines\":[");
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                if (lineIndex > 0)
                {
                    builder.Append(',');
                }

                PdfDataExtractor.LayoutLine line = lines[lineIndex];
                builder.Append('{');
                AppendStringProperty(builder, "text", line.Text);
                builder.Append(',');
                AppendDoubleProperty(builder, "x", line.Left);
                builder.Append(',');
                AppendDoubleProperty(builder, "y", line.Top);
                builder.Append(',');
                AppendDoubleProperty(builder, "width", line.Width);
                builder.Append(',');
                AppendDoubleProperty(builder, "height", line.Height);
                builder.Append('}');
            }

            builder.Append(']');
        }

        private static void AppendStringArrayProperty(StringBuilder builder, string name, IReadOnlyList<string> values)
        {
            builder.Append('"').Append(Escape(name)).Append("\":[");
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                AppendStringValue(builder, values[index]);
            }

            builder.Append(']');
        }

        private static void AppendStringProperty(StringBuilder builder, string name, string value)
        {
            builder.Append('"').Append(Escape(name)).Append("\":");
            AppendStringValue(builder, value);
        }

        private static void AppendStringValue(StringBuilder builder, string value)
        {
            builder.Append('"').Append(Escape(value ?? string.Empty)).Append('"');
        }

        private static void AppendNumberProperty(StringBuilder builder, string name, int value)
        {
            builder.Append('"').Append(Escape(name)).Append("\":");
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendDoubleProperty(StringBuilder builder, string name, double value)
        {
            builder.Append('"').Append(Escape(name)).Append("\":");
            builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 8);
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append(@"\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\b':
                        builder.Append(@"\b");
                        break;
                    case '\f':
                        builder.Append(@"\f");
                        break;
                    case '\n':
                        builder.Append(@"\n");
                        break;
                    case '\r':
                        builder.Append(@"\r");
                        break;
                    case '\t':
                        builder.Append(@"\t");
                        break;
                    default:
                        if (ch < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(ch);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
