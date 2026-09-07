using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using HL7Soup.Integrations.ExtensionBridge;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 5) throw new ArgumentException("Expected mode, manifest, executable, provider and revision.");
            string mode = args[0], manifest = Path.GetFullPath(args[1]), executable = Path.GetFullPath(args[2]), provider = args[3], revision = args[4];
            if (!Path.IsPathRooted(args[1]) || !Path.IsPathRooted(args[2]) || !Regex.IsMatch(provider, "^[a-z0-9.-]+$") ||
                !Regex.IsMatch(revision, "^[0-9.]+$") || Path.GetFileName(manifest) != provider + ".json")
                throw new ArgumentException("Invalid installer-owned manifest or executable.");
            string backup = manifest + "." + revision + ".rollback";
            if (mode == "rollback")
            {
                if (File.Exists(backup)) { if (File.Exists(manifest)) File.Replace(backup, manifest, null); else File.Move(backup, manifest); }
                return 0;
            }
            if (mode == "commit") { if (File.Exists(backup)) File.Delete(backup); return 0; }
            if (mode != "write" || !File.Exists(manifest) || !File.Exists(executable)) throw new ArgumentException("Invalid manifest write operation.");
            // Back up before mutation; rollback is scheduled before this action.
            // A stale backup is an error, never overwritten or silently discarded.
            File.Copy(manifest, backup, false);
            var value = new ExtensionProviderManifest { ProviderId = provider, Revision = revision, ExecutablePath = executable, Transport = "NamedPipe", TimeoutSeconds = 180 };
            string temporary = manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(ExtensionProviderManifest)).WriteObject(stream, value);
                    stream.Flush(true);
                }
                File.Replace(temporary, manifest, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("Provider registration failed: " + ex.GetType().Name); return 1; }
    }
}
