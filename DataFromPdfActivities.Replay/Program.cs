using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataFromPdfActivities.Runner;

internal static class Program
{
    private static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions { WriteIndented = true };

    private static void Main(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Usage: replay <Sample1 JSON> <sample2 JSON> <saved workflow> <output directory>");
        var inputHashes = args.Take(3).Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
        SyntheticChecks();
        Directory.CreateDirectory(args[3]);
        // The saved workflow contains repeated metadata properties. JsonDocument can read
        // them without rewriting or normalizing the workflow, which remains read-only.
        using var workflowDocument = JsonDocument.Parse(File.ReadAllText(args[2]));
        var workflow = workflowDocument.RootElement;
        Assert(workflow.GetProperty("Name").GetString() == "PDF Referral to HL7", "Unexpected workflow");
        var activities = Objects(workflow).Where(node => node.TryGetProperty("Name", out var name) && name.GetString() == "Convert to JSON" && node.TryGetProperty("ResponseMessageTemplate", out _)).ToList();
        Assert(activities.Count == 1, "Expected exactly one Convert to JSON response template");
        string template = activities[0].GetProperty("ResponseMessageTemplate").GetString();
        var summary = new JsonArray();
        Replay("Sample1", File.ReadAllText(args[0]), args[3], summary, false);
        Replay("sample2", File.ReadAllText(args[1]), args[3], summary, false);
        Replay("successful-template", template, args[3], summary, true);
        Assert(args.Take(3).Select((path, i) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == inputHashes[i]).All(same => same), "Source input changed during replay");
        var report = new JsonObject
        {
            ["representation"] = "pages[].lines[].text/x/y plus existing data.fields; exact shared production recovery source, no reconstructed words or segments",
            ["scope"] = "Recovery-only JSON replay, not PDF ingestion. All other JSON nodes preserved. Historical template is not a new workflow run.",
            ["syntheticChecks"] = "passed",
            ["sourceFilesUnchangedDuringReplay"] = true,
            ["inputs"] = new JsonArray(args.Take(3).Select(path => (JsonNode)new JsonObject { ["path"] = Path.GetFullPath(path), ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }).ToArray()),
            ["results"] = summary
        };
        File.WriteAllText(Path.Combine(args[3], "verification.json"), report.ToJsonString(Pretty));
        Console.WriteLine("Synthetic regression checks and all three JSON replays passed; full private results written to output directory.");
    }

    private static IEnumerable<JsonElement> Objects(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            foreach (var child in node.EnumerateObject()) foreach (var nested in Objects(child.Value)) yield return nested;
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) foreach (var nested in Objects(child)) yield return nested;
    }

    private static Dictionary<string, string> Fields(JsonNode json) => json["data"]["fields"].AsObject()
        .ToDictionary(pair => pair.Key, pair => pair.Value.GetValue<string>(), StringComparer.OrdinalIgnoreCase);

    private static void Recover(JsonNode json, Dictionary<string, string> fields)
    {
        foreach (var page in json["pages"].AsArray())
            UnlabeledColumnRecovery.AddMissingBlocks(fields, page["number"].GetValue<int>(), page["lines"].AsArray()
                .Select(line => new UnlabeledColumnRecovery.Line(line["text"].GetValue<string>(), line["x"].GetValue<double>(), line["y"].GetValue<double>())).ToList());
    }

    private static void Replay(string name, string input, string output, JsonArray summary, bool unchanged)
    {
        var original = JsonNode.Parse(input);
        var result = original.DeepClone();
        var before = Fields(original);
        var after = Fields(result);
        Recover(result, after);
        Assert(before.All(pair => after.TryGetValue(pair.Key, out var value) && value == pair.Value), name + ": original fields changed");
        var added = after.Where(pair => !before.ContainsKey(pair.Key)).ToArray();
        Assert(unchanged ? added.Length == 0 : added.Length == 5, name + ": unexpected recovery count");
        if (!unchanged)
        {
            string anchorKey = name == "Sample1" ? "unlabeledAfterVisitNo" : "unlabeledAfterAcc";
            Assert(!after.Keys.Any(key => key.StartsWith("recoveredUnlabeledBlock", StringComparison.Ordinal)), name + ": obsolete generic key");
            Assert(added.Any(pair => pair.Key == anchorKey + "Line1"), name + ": missing first block line");
            Assert(added.Any(pair => pair.Key == anchorKey + "Line3"), name + ": missing third block line");
            // Dataset-specific expectations belong only in this replay harness, never production.
            var sourceLines = original["pages"][0]["lines"].AsArray();
            if (name == "Sample1")
            {
                Assert(after[anchorKey + "Line1"] == "Minnie H Mouse", "Sample1 name recovery");
                Assert(after[anchorKey + "Line2"] == sourceLines[6]["text"].GetValue<string>().Split("Priority:")[0].Trim(), "Sample1 address recovery");
                Assert(after[anchorKey + "Line3"] == sourceLines[7]["text"].GetValue<string>(), "Sample1 last block row preserved");
            }
            else
            {
                Assert(after[anchorKey + "Line1"] == "No Body Body", "sample2 name recovery");
                Assert(after[anchorKey + "Line2"] == sourceLines[11]["text"].GetValue<string>() && after[anchorKey + "Line3"] == sourceLines[12]["text"].GetValue<string>(), "sample2 address recovery");
            }
        }
        var once = new Dictionary<string, string>(after, StringComparer.OrdinalIgnoreCase);
        Recover(result, after);
        Assert(once.Count == after.Count && once.All(pair => after[pair.Key] == pair.Value), name + ": replay is not idempotent");
        foreach (var pair in added) result["data"]["fields"][pair.Key] = pair.Value;
        var stripped = result.DeepClone();
        foreach (var pair in added) stripped["data"]["fields"].AsObject().Remove(pair.Key);
        Assert(JsonNode.DeepEquals(original, stripped), name + ": unrelated JSON changed");
        File.WriteAllText(Path.Combine(output, name + ".replayed.json"), result.ToJsonString(Pretty));
        // The comparison is deliberately local: line values may still contain residual private data.
        File.WriteAllText(Path.Combine(output, name + ".field-comparison.json"), new JsonObject
        {
            ["originalFieldCount"] = before.Count, ["originalFieldsUnchanged"] = true,
            ["addedFields"] = JsonSerializer.SerializeToNode(added.ToDictionary(pair => pair.Key, pair => pair.Value)),
            ["changedFields"] = new JsonArray(), ["removedFields"] = new JsonArray()
        }.ToJsonString(Pretty));
        summary.Add(new JsonObject { ["name"] = name, ["originalFields"] = before.Count, ["addedFields"] = added.Length,
            ["addedKeys"] = JsonSerializer.SerializeToNode(added.Select(pair => pair.Key).ToArray()),
            ["originalFieldsUnchanged"] = true, ["allOtherJsonUnchanged"] = true, ["idempotent"] = true });
    }

    private static void SyntheticChecks()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ticketNo"] = "456", ["code"] = "", ["priority"] = "Soon" };
        var mixed = new[] { L("Ticket No: 456", 300, 650), L("Example Person Code:", 70, 636), L("10 Example Road Priority: Soon", 70, 622), L("Example Town", 70, 608), L("Unknown: stop", 70, 594) };
        UnlabeledColumnRecovery.AddMissingBlocks(fields, 1, mixed);
        Assert(fields["unlabeledAfterTicketNoLine1"] == "Example Person" && fields["unlabeledAfterTicketNoLine2"] == "10 Example Road", "Mixed rows must exclude right labels/values");
        Assert(fields["code"] == "" && fields["ticketNo"] == "456", "Existing values including blanks preserved");
        int count = fields.Count;
        UnlabeledColumnRecovery.AddMissingBlocks(fields, 1, mixed);
        Assert(fields.Count == count, "Duplicate recovery");
        var plain = new[] { L("Code: filled", 300, 650), L("Example Person", 70, 636), L("10 Example Road", 70, 622), L("Example Town", 70, 608) };
        var populated = new Dictionary<string, string> { ["code"] = "filled" };
        UnlabeledColumnRecovery.AddMissingBlocks(populated, 2, plain);
        Assert(populated["unlabeledAfterCodePage"] == "2", "Populated anchor recovery");
        var legacy = new Dictionary<string, string> { ["code"] = "", ["unlabeledAfterCode"] = "Example Person 10 Example Road Example Town" };
        UnlabeledColumnRecovery.AddMissingBlocks(legacy, 1, plain);
        Assert(legacy.Count == 2, "Legacy block duplicated");
        var collision = new Dictionary<string, string> { ["code"] = "filled", ["unlabeledAfterCode"] = "preserve", ["unlabeledAfterCodeLine1"] = "" };
        UnlabeledColumnRecovery.AddMissingBlocks(collision, 1, plain);
        Assert(collision["unlabeledAfterCode"] == "preserve" && collision["unlabeledAfterCodeLine1"] == "" && collision.ContainsKey("unlabeledAfterCodeBlock2"), "Key collision overwrite");
        Assert(!collision.ContainsKey("unlabeledAfterCodeLine2") && collision["unlabeledAfterCodeBlock2Line1"] == "Example Person", "Collision merged unrelated blocks");
        int collisionCount = collision.Count;
        UnlabeledColumnRecovery.AddMissingBlocks(collision, 1, plain);
        Assert(collision.Count == collisionCount, "Collision recovery is not idempotent");
        foreach (string reserved in new[] { "unlabeledAfterCode", "UNLABELEDAFTERCODELINE1", "unlabeledAfterCodePage" })
        {
            var partial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["code"] = "filled", [reserved] = "", ["unlabeledAfterCodeBlock2Line1"] = "unrelated" };
            UnlabeledColumnRecovery.AddMissingBlocks(partial, 1, plain);
            Assert(partial[reserved] == "" && partial["unlabeledAfterCodeBlock2Line1"] == "unrelated" && partial["unlabeledAfterCodeBlock3Line1"] == "Example Person", "Partial/case-insensitive collision overwrite");
            Assert(partial.Count == 8, "Collision must add exactly one complete field family");
        }
        foreach (var rows in new[] {
            new[] { L("Code: filled", 70, 650), L("ordinary continuation", 70, 636), L("more prose", 70, 622) },
            new[] { L("Code: filled", 300, 650), L("distant heading", 70, 600), L("more prose", 70, 586) },
            new[] { L("Code: filled", 300, 650), L("single heading", 70, 636), L("Unknown: value", 70, 622) },
            new[] { L("", 300, 650), L("unanchored text", 70, 636), L("more text", 70, 622) },
            new[] { L("Unknown: filled", 300, 650), L("unanchored text", 70, 636), L("more text", 70, 622) } })
        {
            var negative = new Dictionary<string, string> { ["code"] = "filled" };
            UnlabeledColumnRecovery.AddMissingBlocks(negative, 1, rows);
            Assert(negative.Count == 1, "Conservative boundary failed");
        }
    }

    private static UnlabeledColumnRecovery.Line L(string text, double x, double y) => new UnlabeledColumnRecovery.Line(text, x, y);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
