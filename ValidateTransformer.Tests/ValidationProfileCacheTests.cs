using HL7Soup.Integrations;
using ValidateTransformer;

namespace ValidateTransformer.Tests;

public sealed class ValidationProfileCacheTests
{
    [Fact]
    public void Changed_profile_file_is_loaded_on_the_next_validation_without_leaving_alias_files()
    {
        string validatorDirectory = Path.Combine(Path.GetTempPath(), "ValidateTransformerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(validatorDirectory);
        string sourcePath = Path.Combine(validatorDirectory, "Reload Test.HL7SoupValidators");
        SnapshotValidationMessage message = new SnapshotValidationMessage(validatorDirectory);

        try
        {
            File.WriteAllText(sourcePath, "first profile");
            ValidationProfileCache.ValidateWithLatestProfile(message, "Reload Test", validatorDirectory);
            string firstEffectiveName = message.LastProfileName;
            Assert.Equal("first profile", message.LastSnapshotText);
            Assert.NotEqual("Reload Test", firstEffectiveName);

            DateTime originalWriteTime = File.GetLastWriteTimeUtc(sourcePath);
            File.WriteAllText(sourcePath, "other profile");
            File.SetLastWriteTimeUtc(sourcePath, originalWriteTime);
            ValidationProfileCache.ValidateWithLatestProfile(message, "Reload Test", validatorDirectory);
            Assert.Equal("other profile", message.LastSnapshotText);
            Assert.NotEqual(firstEffectiveName, message.LastProfileName);

            string secondEffectiveName = message.LastProfileName;
            ValidationProfileCache.ValidateWithLatestProfile(message, "Reload Test", validatorDirectory);
            Assert.Equal(secondEffectiveName, message.LastProfileName);
            Assert.Equal("other profile", message.LastSnapshotText);

            Assert.Equal(new[] { "Reload Test.HL7SoupValidators" }, Directory.GetFiles(validatorDirectory).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(validatorDirectory, true);
        }
    }

    private sealed class SnapshotValidationMessage : IHL7Message
    {
        private readonly string validatorDirectory;
        private readonly Dictionary<string, string> snapshots = new(StringComparer.OrdinalIgnoreCase);

        internal SnapshotValidationMessage(string validatorDirectory)
        {
            this.validatorDirectory = validatorDirectory;
        }

        internal string LastProfileName { get; private set; } = string.Empty;
        internal string LastSnapshotText { get; private set; } = string.Empty;
        public string Text { get; private set; } = string.Empty;

        public string ValidateWithHighlighters(string profileName)
        {
            LastProfileName = profileName;
            string path = Path.Combine(validatorDirectory, profileName + ".HL7SoupValidators");
            if (File.Exists(path))
            {
                snapshots[profileName] = File.ReadAllText(path);
            }
            LastSnapshotText = snapshots[profileName];
            return "{\"Profile\":\"" + profileName + "\",\"Errors\":[]}";
        }

        public bool ValidatesWithHighlighters(string profileName) => throw new NotSupportedException();
        public IHL7Segments GetSegments() => throw new NotSupportedException();
        public IHL7Segments GetSegments(string header) => throw new NotSupportedException();
        public IHL7Segment GetSegment(string locationCode) => throw new NotSupportedException();
        public void AddSegment(IHL7Segment segment) => throw new NotSupportedException();
        public void AddSegment(string segment) => throw new NotSupportedException();
        public IHL7Part GetPart(string path) => throw new NotSupportedException();
        public void RemoveSegment(IHL7Segment segment) => throw new NotSupportedException();
        public void BeginUpdate() => throw new NotSupportedException();
        public void EndUpdate() => throw new NotSupportedException();
        public string GetValueAtPath(string path) => throw new NotSupportedException();
        public void SetValueAtPath(string toPath, string fromValue) => throw new NotSupportedException();
        public void SetStructureAtPath(string toPath, string fromValue) => throw new NotSupportedException();
        public void SetText(string text) => Text = text;
        public void Dispose()
        {
        }
    }
}
