using System;
using System.IO;

namespace Popokey.ExtensionRunners
{
    internal static class PersistentRunnerExecutableResolver
    {
        internal static string ResolveExecutablePath(Type anchorType, string environmentVariableName, string runnerDirectoryName, string executableName, string runnerDescription)
        {
            if (anchorType == null)
            {
                throw new ArgumentNullException(nameof(anchorType));
            }

            string configuredPath = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullConfiguredPath = Path.GetFullPath(configuredPath);
                if (File.Exists(fullConfiguredPath))
                {
                    return fullConfiguredPath;
                }

                throw new FileNotFoundException(
                    $"The {runnerDescription} configured by {environmentVariableName} was not found at '{fullConfiguredPath}'.",
                    fullConfiguredPath);
            }

            string activityDirectory = Path.GetDirectoryName(anchorType.Assembly.Location) ?? string.Empty;
            string hostDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string[] candidatePaths =
            {
                Path.Combine(activityDirectory, runnerDirectoryName, executableName),
                Path.Combine(hostDirectory, runnerDirectoryName, executableName)
            };

            foreach (string candidatePath in candidatePaths)
            {
                if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            throw new FileNotFoundException(
                $"The {runnerDescription} executable could not be found. Place '{executableName}' under a '{runnerDirectoryName}' folder beside the activity DLL, or set {environmentVariableName}.");
        }
    }
}
