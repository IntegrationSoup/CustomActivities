using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace ZipActivities.Runner
{
    internal static class ZipArchiveOperations
    {
        internal static ZipOperationResult CreateFile(string sourceDirectory, string zipFilePath, bool includeBaseDirectory, bool overwriteExistingFile)
        {
            string sourceRoot = ResolveSourceDirectory(sourceDirectory);
            string outputPath = ResolveRequiredPath(zipFilePath, "ZIP File Path");

            if (File.Exists(outputPath) && !overwriteExistingFile)
            {
                throw new IOException($"The ZIP file '{outputPath}' already exists. Enable Overwrite Existing File to replace it.");
            }

            string outputDirectory = Path.GetDirectoryName(outputPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(outputDirectory);

            string temporaryPath = Path.Combine(
                outputDirectory,
                "." + Path.GetFileName(outputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                ZipOperationResult result;
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    result = CreateArchive(stream, sourceRoot, includeBaseDirectory, outputPath, temporaryPath);
                }

                if (File.Exists(outputPath))
                {
                    if (!overwriteExistingFile)
                    {
                        throw new IOException($"The ZIP file '{outputPath}' was created by another process. Enable Overwrite Existing File to replace it.");
                    }

                    File.Replace(temporaryPath, outputPath, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, outputPath);
                }

                result.ZipLength = new FileInfo(outputPath).Length;
                return result;
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        internal static ZipBytesResult CreateBytes(string sourceDirectory, bool includeBaseDirectory)
        {
            string sourceRoot = ResolveSourceDirectory(sourceDirectory);
            using (MemoryStream stream = new MemoryStream())
            {
                ZipOperationResult result = CreateArchive(stream, sourceRoot, includeBaseDirectory, null, null);
                byte[] bytes = stream.ToArray();
                result.ZipLength = bytes.LongLength;
                return new ZipBytesResult(result, bytes);
            }
        }

        internal static ZipOperationResult Extract(string zipFilePath, string destinationDirectory, bool overwriteExistingFiles)
        {
            string archivePath = ResolveRequiredPath(zipFilePath, "ZIP File Path");
            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException($"The ZIP file was not found: '{archivePath}'.", archivePath);
            }

            string destinationRoot = ResolveRequiredPath(destinationDirectory, "Destination Directory");
            string destinationPrefix = EnsureTrailingSeparator(destinationRoot);

            using (FileStream stream = File.OpenRead(archivePath))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
            {
                List<ExtractionTarget> targets = BuildExtractionPlan(archive, destinationRoot, destinationPrefix, overwriteExistingFiles);
                Directory.CreateDirectory(destinationRoot);

                int fileCount = 0;
                int directoryCount = 0;
                foreach (ExtractionTarget target in targets)
                {
                    if (target.IsDirectory)
                    {
                        Directory.CreateDirectory(target.Path);
                        directoryCount++;
                        continue;
                    }

                    string parentDirectory = Path.GetDirectoryName(target.Path);
                    if (!string.IsNullOrEmpty(parentDirectory))
                    {
                        Directory.CreateDirectory(parentDirectory);
                    }

                    FileMode mode = overwriteExistingFiles ? FileMode.Create : FileMode.CreateNew;
                    using (Stream input = target.Entry.Open())
                    using (FileStream output = new FileStream(target.Path, mode, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }

                    TrySetLastWriteTime(target.Path, target.Entry.LastWriteTime);
                    fileCount++;
                }

                return new ZipOperationResult
                {
                    FileCount = fileCount,
                    DirectoryCount = directoryCount,
                    ZipLength = stream.Length
                };
            }
        }

        private static ZipOperationResult CreateArchive(Stream output, string sourceRoot, bool includeBaseDirectory, params string[] excludedPaths)
        {
            HashSet<string> exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string excludedPath in excludedPaths ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(excludedPath))
                {
                    exclusions.Add(Path.GetFullPath(excludedPath));
                }
            }

            DirectoryInfo sourceInfo = new DirectoryInfo(sourceRoot);
            string baseName = includeBaseDirectory ? sourceInfo.Name : string.Empty;
            if (includeBaseDirectory && string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "archive";
            }

            ZipOperationResult result = new ZipOperationResult();
            using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                if (includeBaseDirectory)
                {
                    archive.CreateEntry(ToZipDirectoryName(baseName));
                    result.DirectoryCount++;
                }

                AddDirectoryContents(archive, sourceRoot, sourceRoot, baseName, exclusions, result);
            }

            return result;
        }

        private static void AddDirectoryContents(
            ZipArchive archive,
            string sourceRoot,
            string currentDirectory,
            string baseName,
            HashSet<string> exclusions,
            ZipOperationResult result)
        {
            foreach (string directoryPath in Directory.GetDirectories(currentDirectory))
            {
                if (IsReparsePoint(directoryPath))
                {
                    continue;
                }

                string entryName = BuildEntryName(sourceRoot, directoryPath, baseName);
                archive.CreateEntry(ToZipDirectoryName(entryName));
                result.DirectoryCount++;

                AddDirectoryContents(archive, sourceRoot, directoryPath, baseName, exclusions, result);
            }

            foreach (string filePath in Directory.GetFiles(currentDirectory))
            {
                string fullFilePath = Path.GetFullPath(filePath);
                if (exclusions.Contains(fullFilePath) || IsReparsePoint(filePath))
                {
                    continue;
                }

                string entryName = BuildEntryName(sourceRoot, filePath, baseName);
                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTime(filePath);

                using (FileStream input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Stream entryStream = entry.Open())
                {
                    input.CopyTo(entryStream);
                }

                result.FileCount++;
            }
        }

        private static List<ExtractionTarget> BuildExtractionPlan(
            ZipArchive archive,
            string destinationRoot,
            string destinationPrefix,
            bool overwriteExistingFiles)
        {
            List<ExtractionTarget> targets = new List<ExtractionTarget>();
            Dictionary<string, bool> plannedPaths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string entryName = entry.FullName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    continue;
                }

                if (entryName.IndexOf(':') >= 0)
                {
                    throw new InvalidDataException($"ZIP entry '{entryName}' contains an invalid path.");
                }

                string relativePath = entryName
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                string targetPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
                if (!targetPath.Equals(destinationRoot, StringComparison.OrdinalIgnoreCase)
                    && !targetPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"ZIP entry '{entryName}' would be extracted outside the destination directory.");
                }

                bool isDirectory = entryName.EndsWith("/", StringComparison.Ordinal)
                    || entryName.EndsWith("\\", StringComparison.Ordinal);

                if (plannedPaths.TryGetValue(targetPath, out bool existingIsDirectory))
                {
                    if (!isDirectory || existingIsDirectory != isDirectory)
                    {
                        throw new InvalidDataException($"ZIP entry '{entryName}' duplicates another archive path.");
                    }

                    continue;
                }

                plannedPaths.Add(targetPath, isDirectory);
                if (!isDirectory && File.Exists(targetPath) && !overwriteExistingFiles)
                {
                    throw new IOException($"The destination file '{targetPath}' already exists. Enable Overwrite Existing Files to replace it.");
                }

                targets.Add(new ExtractionTarget(entry, targetPath, isDirectory));
            }

            return targets;
        }

        private static string ResolveSourceDirectory(string sourceDirectory)
        {
            string sourceRoot = ResolveRequiredPath(sourceDirectory, "Source Directory");
            if (!Directory.Exists(sourceRoot))
            {
                throw new DirectoryNotFoundException($"The source directory was not found: '{sourceRoot}'.");
            }

            return sourceRoot;
        }

        private static string ResolveRequiredPath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException($"{parameterName} is required.");
            }

            return Path.GetFullPath(path.Trim());
        }

        private static string BuildEntryName(string sourceRoot, string path, string baseName)
        {
            string relativePath = Path.GetFullPath(path).Substring(EnsureTrailingSeparator(sourceRoot).Length);
            string entryName = string.IsNullOrEmpty(baseName)
                ? relativePath
                : Path.Combine(baseName, relativePath);

            return entryName.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        private static string ToZipDirectoryName(string entryName)
        {
            return entryName.TrimEnd('/', '\\').Replace('\\', '/') + "/";
        }

        private static string EnsureTrailingSeparator(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullPath
                : fullPath + Path.DirectorySeparatorChar;
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        private static void TrySetLastWriteTime(string path, DateTimeOffset timestamp)
        {
            try
            {
                File.SetLastWriteTime(path, timestamp.LocalDateTime);
            }
            catch
            {
                // Timestamp preservation is best-effort only.
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }

        private sealed class ExtractionTarget
        {
            internal ExtractionTarget(ZipArchiveEntry entry, string path, bool isDirectory)
            {
                Entry = entry;
                Path = path;
                IsDirectory = isDirectory;
            }

            internal ZipArchiveEntry Entry { get; }
            internal string Path { get; }
            internal bool IsDirectory { get; }
        }
    }

    internal sealed class ZipOperationResult
    {
        internal int FileCount { get; set; }
        internal int DirectoryCount { get; set; }
        internal long ZipLength { get; set; }
    }

    internal sealed class ZipBytesResult
    {
        internal ZipBytesResult(ZipOperationResult result, byte[] bytes)
        {
            Result = result;
            Bytes = bytes;
        }

        internal ZipOperationResult Result { get; }
        internal byte[] Bytes { get; }
    }
}
