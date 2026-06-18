using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HL7ValueTransformers
{
    public static class Hl7ValueFormatter
    {
        private static readonly Regex RepeatedWhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly Regex StatePostcodeLocalityPattern = new Regex(
            @"^(?<city>.+?)\s+(?<state>[A-Za-z]{2,3})\s+(?<postcode>\d{4})$",
            RegexOptions.Compiled);

        private static readonly Regex PostcodeLocalityPattern = new Regex(
            @"^(?<city>.+?)\s+(?<postcode>\d{4})$",
            RegexOptions.Compiled);

        private static readonly Regex CityStatePattern = new Regex(
            @"^(?<city>.+?)\s+(?<state>[A-Za-z]{2,3})$",
            RegexOptions.Compiled);

        private static readonly Regex PostcodePattern = new Regex(
            @"^\d{4}$",
            RegexOptions.Compiled);

        private static readonly Regex NonPhoneCharacterPattern = new Regex(
            @"[^\d+]",
            RegexOptions.Compiled);

        private static readonly Regex PhoneExtensionPattern = new Regex(
            @"(?:\s+(?:extn?|extension|x)\.?\s*|\s*#\s*)(?<extension>\d{1,8})\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly IReadOnlyDictionary<string, string> NamePrefixes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dr"] = "Dr",
                ["Mr"] = "Mr",
                ["Mrs"] = "Mrs",
                ["Ms"] = "Ms",
                ["Miss"] = "Miss",
                ["Prof"] = "Prof",
                ["Professor"] = "Prof"
            };

        public static string FormatDigitsOnly(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        public static string FormatLettersAndDigitsOnly(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        public static string FormatLettersDigitsAndSpacesOnly(string value)
        {
            char[] filtered = (value ?? string.Empty)
                .Select(character =>
                    char.IsLetterOrDigit(character)
                        ? char.ToUpperInvariant(character)
                        : char.IsWhiteSpace(character)
                            ? ' '
                            : '\0')
                .Where(character => character != '\0')
                .ToArray();

            return RepeatedWhitespacePattern.Replace(new string(filtered), " ").Trim();
        }

        public static string FormatHl7Xpn(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            if (cleaned.Contains("^"))
            {
                return string.Join("^", cleaned.Split('^').Select(EscapeHl7).ToArray());
            }

            if (cleaned.Contains(","))
            {
                string[] parts = cleaned.Split(new[] { ',' }, 2, StringSplitOptions.None)
                    .Select(part => part.Trim())
                    .ToArray();

                return EscapeHl7(parts[0]) + "^" + EscapeHl7(parts.Length > 1 ? parts[1] : string.Empty);
            }

            string[] tokens = SplitWords(cleaned);
            string prefix = ExtractNamePrefix(ref tokens);
            if (tokens.Length == 0)
            {
                return string.IsNullOrWhiteSpace(prefix) ? string.Empty : JoinComponents(string.Empty, string.Empty, string.Empty, string.Empty, EscapeHl7(prefix));
            }

            if (tokens.Length == 1)
            {
                return string.IsNullOrWhiteSpace(prefix)
                    ? EscapeHl7(tokens[0])
                    : JoinComponents(EscapeHl7(tokens[0]), string.Empty, string.Empty, string.Empty, EscapeHl7(prefix));
            }

            string family = tokens[tokens.Length - 1];
            string given = string.Join(" ", tokens.Take(tokens.Length - 1).ToArray());
            return JoinComponents(EscapeHl7(family), EscapeHl7(given), string.Empty, string.Empty, EscapeHl7(prefix));
        }

        public static string FormatHl7Xad(string value)
        {
            string cleaned = value ?? string.Empty;
            string[] lines = cleaned
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();

            if (lines.Length == 1 && lines[0].Contains(","))
            {
                lines = lines[0]
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0)
                    .ToArray();
            }

            if (lines.Length == 0)
            {
                return EscapeHl7(cleaned);
            }

            Hl7AddressComponents address;
            if (TryParseAddressLines(lines, out address))
            {
                return JoinComponents(
                    EscapeHl7(address.StreetAddress),
                    EscapeHl7(address.OtherDesignation),
                    EscapeHl7(address.City),
                    EscapeHl7(address.StateOrProvince.ToUpperInvariant()),
                    EscapeHl7(address.PostalCode));
            }

            return string.Join("^^", lines.Select(EscapeHl7).ToArray());
        }

        public static string FormatHl7Xtn(string value)
        {
            PhoneParts phone = ExtractPhoneExtension(value ?? string.Empty);
            string withoutWhitespace = RepeatedWhitespacePattern.Replace(phone.Number, string.Empty);
            string cleaned = NormalizeInternationalPhonePrefix(NonPhoneCharacterPattern.Replace(withoutWhitespace, string.Empty));

            if (string.IsNullOrWhiteSpace(phone.Extension))
            {
                return EscapeHl7(cleaned);
            }

            return JoinComponents(
                EscapeHl7(cleaned),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                EscapeHl7(phone.Extension));
        }

        public static string EscapeHl7(string value)
        {
            return (value ?? string.Empty)
                .Replace(@"\", @"\E\")
                .Replace("|", @"\F\")
                .Replace("^", @"\S\")
                .Replace("&", @"\T\")
                .Replace("~", @"\R\")
                .Replace("\r\n", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private static string[] SplitWords(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length > 0)
                .ToArray();
        }

        private static string ExtractNamePrefix(ref string[] tokens)
        {
            if (tokens.Length <= 1)
            {
                return string.Empty;
            }

            string firstToken = tokens[0].TrimEnd('.');
            string prefix;
            if (!NamePrefixes.TryGetValue(firstToken, out prefix))
            {
                return string.Empty;
            }

            tokens = tokens.Skip(1).ToArray();
            return prefix;
        }

        private static bool TryParseAddressLines(IReadOnlyList<string> lines, out Hl7AddressComponents address)
        {
            address = null;
            if (lines.Count < 2)
            {
                return false;
            }

            string city;
            string stateOrProvince;
            string postalCode;
            string localityLine = lines[lines.Count - 1];
            if (!TryParseLocalityLine(localityLine, out city, out stateOrProvince, out postalCode))
            {
                if (!TryParseSeparatedPostcodeLocality(lines, out city, out stateOrProvince, out postalCode))
                {
                    return false;
                }

                address = BuildAddressComponents(lines, 2, city, stateOrProvince, postalCode);
                return true;
            }

            address = BuildAddressComponents(lines, 1, city, stateOrProvince, postalCode);
            return true;
        }

        private static Hl7AddressComponents BuildAddressComponents(
            IReadOnlyList<string> lines,
            int localityLineCount,
            string city,
            string stateOrProvince,
            string postalCode)
        {
            int streetIndex = Math.Max(0, lines.Count - localityLineCount - 1);
            string streetAddress = lines[streetIndex];
            string otherDesignation = streetIndex > 0
                ? string.Join(", ", lines.Take(streetIndex).ToArray())
                : string.Empty;

            return new Hl7AddressComponents(streetAddress, otherDesignation, city, stateOrProvince, postalCode);
        }

        private static bool TryParseLocalityLine(
            string value,
            out string city,
            out string stateOrProvince,
            out string postalCode)
        {
            Match statePostcode = StatePostcodeLocalityPattern.Match((value ?? string.Empty).Trim());
            if (statePostcode.Success)
            {
                city = statePostcode.Groups["city"].Value.Trim();
                stateOrProvince = statePostcode.Groups["state"].Value.Trim();
                postalCode = statePostcode.Groups["postcode"].Value.Trim();
                return true;
            }

            Match postcodeOnly = PostcodeLocalityPattern.Match((value ?? string.Empty).Trim());
            if (postcodeOnly.Success)
            {
                city = postcodeOnly.Groups["city"].Value.Trim();
                stateOrProvince = string.Empty;
                postalCode = postcodeOnly.Groups["postcode"].Value.Trim();
                return true;
            }

            city = string.Empty;
            stateOrProvince = string.Empty;
            postalCode = string.Empty;
            return false;
        }

        private static bool TryParseSeparatedPostcodeLocality(
            IReadOnlyList<string> lines,
            out string city,
            out string stateOrProvince,
            out string postalCode)
        {
            city = string.Empty;
            stateOrProvince = string.Empty;
            postalCode = string.Empty;

            if (lines.Count < 3 || !PostcodePattern.IsMatch(lines[lines.Count - 1].Trim()))
            {
                return false;
            }

            postalCode = lines[lines.Count - 1].Trim();
            string locality = lines[lines.Count - 2].Trim();
            Match cityState = CityStatePattern.Match(locality);
            if (cityState.Success)
            {
                city = cityState.Groups["city"].Value.Trim();
                stateOrProvince = cityState.Groups["state"].Value.Trim();
                return true;
            }

            city = locality;
            return !string.IsNullOrWhiteSpace(city);
        }

        private static PhoneParts ExtractPhoneExtension(string value)
        {
            Match extensionMatch = PhoneExtensionPattern.Match(value ?? string.Empty);
            if (!extensionMatch.Success)
            {
                return new PhoneParts(value ?? string.Empty, string.Empty);
            }

            string extension = new string(extensionMatch.Groups["extension"].Value.Where(char.IsDigit).ToArray());
            return new PhoneParts(value.Substring(0, extensionMatch.Index), extension);
        }

        private static string NormalizeInternationalPhonePrefix(string value)
        {
            string cleaned = value ?? string.Empty;
            if (cleaned.StartsWith("00", StringComparison.Ordinal) && cleaned.Length > 2)
            {
                cleaned = "+" + cleaned.Substring(2);
            }

            cleaned = KeepSingleLeadingPlus(cleaned);

            if (cleaned.StartsWith("+640", StringComparison.Ordinal) ||
                cleaned.StartsWith("+610", StringComparison.Ordinal))
            {
                cleaned = cleaned.Substring(0, 3) + cleaned.Substring(4);
            }

            return cleaned;
        }

        private static string KeepSingleLeadingPlus(string value)
        {
            int firstPlus = (value ?? string.Empty).IndexOf('+');
            if (firstPlus < 0)
            {
                return value ?? string.Empty;
            }

            string withoutPluses = value.Replace("+", string.Empty);
            return firstPlus == 0 ? "+" + withoutPluses : withoutPluses;
        }

        private static string JoinComponents(params string[] components)
        {
            int last = components.Length - 1;
            while (last >= 0 && string.IsNullOrEmpty(components[last]))
            {
                last--;
            }

            return last < 0
                ? string.Empty
                : string.Join("^", components.Take(last + 1).ToArray());
        }

        private sealed class Hl7AddressComponents
        {
            internal Hl7AddressComponents(
                string streetAddress,
                string otherDesignation,
                string city,
                string stateOrProvince,
                string postalCode)
            {
                StreetAddress = streetAddress ?? string.Empty;
                OtherDesignation = otherDesignation ?? string.Empty;
                City = city ?? string.Empty;
                StateOrProvince = stateOrProvince ?? string.Empty;
                PostalCode = postalCode ?? string.Empty;
            }

            internal string StreetAddress { get; }
            internal string OtherDesignation { get; }
            internal string City { get; }
            internal string StateOrProvince { get; }
            internal string PostalCode { get; }
        }

        private sealed class PhoneParts
        {
            internal PhoneParts(string number, string extension)
            {
                Number = number ?? string.Empty;
                Extension = extension ?? string.Empty;
            }

            internal string Number { get; }
            internal string Extension { get; }
        }
    }
}
