using System.IO.Compression;
using ZipActivities.Runner;

namespace ZipActivities.Runner.Tests;

public sealed class ZipArchiveOperationsTests : IDisposable
{
    private readonly string testRoot = Path.Combine(Path.GetTempPath(), "ZipActivities.Runner.Tests", Guid.NewGuid().ToString("N"));

    public ZipArchiveOperationsTests()
    {
        Directory.CreateDirectory(testRoot);
    }

    [Fact]
    public void CreateFileAndExtractPreserveFilesSubdirectoriesAndEmptyDirectories()
    {
        string source = CreateSourceTree();
        string zipPath = Path.Combine(testRoot, "archive.zip");
        string destination = Path.Combine(testRoot, "extracted");

        ZipOperationResult created = ZipArchiveOperations.CreateFile(source, zipPath, includeBaseDirectory: false, overwriteExistingFile: false);
        ZipOperationResult extracted = ZipArchiveOperations.Extract(zipPath, destination, overwriteExistingFiles: false);

        Assert.Equal(2, created.FileCount);
        Assert.Equal(2, created.DirectoryCount);
        Assert.Equal(2, extracted.FileCount);
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(destination, "a.txt")));
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, File.ReadAllBytes(Path.Combine(destination, "nested", "b.bin")));
    }

    [Fact]
    public void CreateBytesCanIncludeTheBaseDirectory()
    {
        string source = CreateSourceTree();

        ZipBytesResult result = ZipArchiveOperations.CreateBytes(source, includeBaseDirectory: true);

        using MemoryStream stream = new MemoryStream(result.Bytes);
        using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
        string prefix = new DirectoryInfo(source).Name + "/";
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "a.txt");
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "empty/");
    }

    [Fact]
    public void CreateFileExcludesItsOwnDestinationWhenDestinationIsInsideSource()
    {
        string source = CreateSourceTree();
        string zipPath = Path.Combine(source, "archive.zip");

        ZipArchiveOperations.CreateFile(source, zipPath, includeBaseDirectory: false, overwriteExistingFile: false);
        ZipArchiveOperations.CreateFile(source, zipPath, includeBaseDirectory: false, overwriteExistingFile: true);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith("archive.zip", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractRejectsEntriesOutsideTheDestinationDirectory()
    {
        string zipPath = Path.Combine(testRoot, "unsafe.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("../outside.txt");
            using StreamWriter writer = new StreamWriter(entry.Open());
            writer.Write("unsafe");
        }

        string destination = Path.Combine(testRoot, "destination");

        Assert.Throws<InvalidDataException>(() => ZipArchiveOperations.Extract(zipPath, destination, overwriteExistingFiles: true));
        Assert.False(File.Exists(Path.Combine(testRoot, "outside.txt")));
    }

    [Fact]
    public void ExtractRequiresOverwriteBeforeReplacingAnExistingFile()
    {
        string source = CreateSourceTree();
        string zipPath = Path.Combine(testRoot, "archive.zip");
        string destination = Path.Combine(testRoot, "destination");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "a.txt"), "existing");
        ZipArchiveOperations.CreateFile(source, zipPath, includeBaseDirectory: false, overwriteExistingFile: false);

        Assert.Throws<IOException>(() => ZipArchiveOperations.Extract(zipPath, destination, overwriteExistingFiles: false));

        ZipArchiveOperations.Extract(zipPath, destination, overwriteExistingFiles: true);
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(destination, "a.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private string CreateSourceTree()
    {
        string source = Path.Combine(testRoot, "source");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "alpha");
        File.WriteAllBytes(Path.Combine(source, "nested", "b.bin"), new byte[] { 0, 1, 2, 255 });
        return source;
    }
}
