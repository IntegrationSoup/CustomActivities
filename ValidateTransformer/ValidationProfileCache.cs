using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security;
using System.Text;

namespace ValidateTransformer
{
    internal static class ValidationProfileCache
    {
        private const string ValidatorExtension = ".HL7SoupValidators";
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, CachedProfile> CachedProfiles =
            new Dictionary<string, CachedProfile>(StringComparer.OrdinalIgnoreCase);

        internal static string ValidateWithLatestProfile(IHL7Message message, string profile)
        {
            string validatorDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Popokey",
                "SharedSettings",
                "Validators");
            return ValidateWithLatestProfile(message, profile, validatorDirectory);
        }

        internal static string ValidateWithLatestProfile(IHL7Message message, string profile, string validatorDirectory)
        {
            lock (SyncRoot)
            {
                string sourcePath = GetProfilePath(profile, validatorDirectory);
                if (!File.Exists(sourcePath))
                {
                    return message.ValidateWithHighlighters(profile);
                }

                StableProfile stableProfile;
                try
                {
                    stableProfile = ReadStableProfile(sourcePath);
                }
                catch (IOException)
                {
                    return message.ValidateWithHighlighters(profile);
                }
                catch (UnauthorizedAccessException)
                {
                    return message.ValidateWithHighlighters(profile);
                }
                catch (SecurityException)
                {
                    return message.ValidateWithHighlighters(profile);
                }

                CachedProfile cachedProfile;
                if (CachedProfiles.TryGetValue(sourcePath, out cachedProfile) && cachedProfile.Signature == stableProfile.Signature)
                {
                    try
                    {
                        return message.ValidateWithHighlighters(cachedProfile.EffectiveProfileName);
                    }
                    catch (Exception exception) when (IsMissingAlias(exception, cachedProfile.EffectiveProfileName))
                    {
                        CachedProfiles.Remove(sourcePath);
                        return message.ValidateWithHighlighters(profile);
                    }
                }

                string effectiveProfileName = BuildEffectiveProfileName(profile, stableProfile.Signature);
                string effectivePath = Path.Combine(Path.GetDirectoryName(sourcePath), effectiveProfileName + ValidatorExtension);
                try
                {
                    File.WriteAllBytes(effectivePath, stableProfile.Content);
                }
                catch (IOException)
                {
                    return message.ValidateWithHighlighters(profile);
                }
                catch (UnauthorizedAccessException)
                {
                    return message.ValidateWithHighlighters(profile);
                }
                catch (SecurityException)
                {
                    return message.ValidateWithHighlighters(profile);
                }

                try
                {
                    try
                    {
                        string result = message.ValidateWithHighlighters(effectiveProfileName);
                        CachedProfiles[sourcePath] = new CachedProfile(stableProfile.Signature, effectiveProfileName);
                        return result;
                    }
                    catch (Exception exception) when (IsMissingAlias(exception, effectiveProfileName))
                    {
                        return message.ValidateWithHighlighters(profile);
                    }
                }
                finally
                {
                    TryDelete(effectivePath);
                }
            }
        }

        private static string GetProfilePath(string profile, string validatorDirectory)
        {
            string safeName = new string(profile.Where(character => !Path.GetInvalidFileNameChars().Contains(character)).ToArray());
            return Path.Combine(validatorDirectory, safeName + ValidatorExtension);
        }

        private static string BuildEffectiveProfileName(string profile, string signature)
        {
            string value = profile + "|" + signature;
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            }

            StringBuilder shortHash = new StringBuilder(24);
            for (int index = 0; index < 12; index++)
            {
                shortHash.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return "__ValidateTransformer_" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "_" + shortHash;
        }

        private static StableProfile ReadStableProfile(string sourcePath)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                FileInfo before = new FileInfo(sourcePath);
                long beforeLength = before.Length;
                long beforeWriteTicks = before.LastWriteTimeUtc.Ticks;

                byte[] content;
                using (FileStream source = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (MemoryStream copy = new MemoryStream())
                {
                    source.CopyTo(copy);
                    content = copy.ToArray();
                }

                FileInfo after = new FileInfo(sourcePath);
                if (after.Length == beforeLength &&
                    after.LastWriteTimeUtc.Ticks == beforeWriteTicks &&
                    content.LongLength == beforeLength)
                {
                    return new StableProfile(ComputeHash(content), content);
                }
            }

            throw new IOException("The validation profile changed while it was being read.");
        }

        private static string ComputeHash(byte[] content)
        {
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(content);
            }

            StringBuilder result = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }

        private static bool IsMissingAlias(Exception exception, string effectiveProfileName)
        {
            return exception != null &&
                   string.Equals(
                       exception.Message,
                       "No highlighters found for profile " + effectiveProfileName,
                       StringComparison.Ordinal);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (SecurityException)
            {
            }
        }

        private sealed class CachedProfile
        {
            internal CachedProfile(string signature, string effectiveProfileName)
            {
                Signature = signature;
                EffectiveProfileName = effectiveProfileName;
            }

            internal string Signature { get; private set; }
            internal string EffectiveProfileName { get; private set; }
        }

        private sealed class StableProfile
        {
            internal StableProfile(string signature, byte[] content)
            {
                Signature = signature;
                Content = content;
            }

            internal string Signature { get; private set; }
            internal byte[] Content { get; private set; }
        }
    }
}
