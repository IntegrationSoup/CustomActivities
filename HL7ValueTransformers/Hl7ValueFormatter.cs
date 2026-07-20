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

        private static readonly Regex LeadingNumericIdentifierPattern = new Regex(
            @"^\s*(?<identifier>\d+)\s+(?<name>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex BracketedIdentifierPattern = new Regex(
            @"\((?<identifier>[A-Za-z0-9]{1,12})\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly IReadOnlyDictionary<string, string> NamePrefixes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dr"] = "Dr",
                ["Doctor"] = "Dr",
                ["Mr"] = "Mr",
                ["Mister"] = "Mr",
                ["Mrs"] = "Mrs",
                ["Ms"] = "Ms",
                ["Miss"] = "Miss",
                ["Master"] = "Master",
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
            return FormatHl7Xpn(value, string.Empty);
        }

        public static string FormatHl7Xpn(string value, string nameOrder)
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

            Hl7NameComponents name = ParsePersonName(cleaned, nameOrder, false, string.Empty);
            return JoinComponents(
                EscapeHl7(name.FamilyName),
                EscapeHl7(name.GivenName),
                EscapeHl7(name.FurtherGivenNames),
                EscapeHl7(name.Suffix),
                EscapeHl7(name.Prefix));
        }

        public static string FormatHl7Xcn(string value, string nameOrder, string personIdentifier)
        {
            string cleaned = (value ?? string.Empty).Trim();
            string identifier = (personIdentifier ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return EscapeHl7(identifier);
            }

            if (cleaned.Contains("^"))
            {
                return FormatExistingHl7Xcn(cleaned, identifier);
            }

            Hl7NameComponents name = ParsePersonName(cleaned, nameOrder, true, identifier);
            return JoinComponents(
                EscapeHl7(name.Identifier),
                EscapeHl7(name.FamilyName),
                EscapeHl7(name.GivenName),
                EscapeHl7(name.FurtherGivenNames),
                EscapeHl7(name.Suffix),
                EscapeHl7(name.Prefix));
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

        private static string FormatExistingHl7Xcn(string value, string personIdentifier)
        {
            string[] components = (value ?? string.Empty).Split('^');
            if (!string.IsNullOrWhiteSpace(personIdentifier))
            {
                components[0] = personIdentifier.Trim();
            }

            return string.Join("^", components.Select(EscapeHl7).ToArray());
        }

        private static Hl7NameComponents ParsePersonName(
            string value,
            string nameOrder,
            bool allowIdentifier,
            string explicitIdentifier)
        {
            string nameText = NormalizeWhitespace(value);
            string identifier = (explicitIdentifier ?? string.Empty).Trim();
            bool hasNameOrder = !string.IsNullOrWhiteSpace(nameOrder);
            bool identifierWasExtractedFromName = false;

            if (allowIdentifier && string.IsNullOrWhiteSpace(identifier))
            {
                identifier = ExtractBracketedIdentifier(ref nameText);
                identifierWasExtractedFromName = !string.IsNullOrWhiteSpace(identifier);
            }

            if (allowIdentifier && !hasNameOrder && string.IsNullOrWhiteSpace(identifier))
            {
                identifier = ExtractLeadingNumericIdentifier(ref nameText);
                identifierWasExtractedFromName = !string.IsNullOrWhiteSpace(identifier);
            }

            Hl7NameComponents name;
            if (hasNameOrder && TryParseNameWithOrder(nameText, nameOrder, allowIdentifier, identifier, identifierWasExtractedFromName, out name))
            {
                return name;
            }

            return ParseNameWithDefaultOrder(nameText, identifier);
        }

        private static bool TryParseNameWithOrder(
            string value,
            string nameOrder,
            bool allowIdentifier,
            string identifier,
            bool identifierWasExtractedFromName,
            out Hl7NameComponents name)
        {
            name = null;
            foreach (NameOrderAlternative alternative in ParseNameOrderAlternatives(nameOrder))
            {
                if (alternative.RequiresComma && !(value ?? string.Empty).Contains(","))
                {
                    continue;
                }

                string working = NormalizeWhitespace(value);
                string prefix = ExtractLeadingNamePrefix(ref working);
                List<string> tokens = SplitNameTokens(working).ToList();
                List<NameOrderField> fields = alternative.Fields
                    .Where(field => field != NameOrderField.Title)
                    .ToList();
                string parsedIdentifier = identifier ?? string.Empty;

                if (fields.Contains(NameOrderField.Identifier))
                {
                    ConsumeIdentifierField(tokens, fields, allowIdentifier, identifierWasExtractedFromName, ref parsedIdentifier);
                }

                fields.RemoveAll(field => field == NameOrderField.Identifier);

                Hl7NameComponents ordered;
                if (!TryBuildOrderedName(tokens, fields, out ordered))
                {
                    continue;
                }

                name = new Hl7NameComponents(
                    parsedIdentifier,
                    ordered.FamilyName,
                    ordered.GivenName,
                    ordered.FurtherGivenNames,
                    ordered.Suffix,
                    prefix);
                return true;
            }

            return false;
        }

        private static IEnumerable<NameOrderAlternative> ParseNameOrderAlternatives(string nameOrder)
        {
            string[] alternatives = (nameOrder ?? string.Empty)
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string alternative in alternatives)
            {
                NameOrderAlternative parsed = ParseNameOrderAlternative(alternative);
                if (parsed.Fields.Count > 0)
                {
                    yield return parsed;
                }
            }
        }

        private static NameOrderAlternative ParseNameOrderAlternative(string value)
        {
            string cleaned = (value ?? string.Empty).Trim();
            bool requiresComma = cleaned.Contains(",");
            List<NameOrderField> fields = new List<NameOrderField>();
            string[] words = cleaned
                .Split(new[] { ' ', '\t', '\r', '\n', ',', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string word in words)
            {
                if (TryAddNameOrderWord(word, fields))
                {
                    continue;
                }
            }

            if (fields.Count == 0)
            {
                string compact = Regex.Replace(cleaned, @"[^A-Za-z]", string.Empty);
                for (int index = 0; index < compact.Length;)
                {
                    if (index + 1 < compact.Length &&
                        string.Equals(compact.Substring(index, 2), "ID", StringComparison.OrdinalIgnoreCase))
                    {
                        fields.Add(NameOrderField.Identifier);
                        index += 2;
                        continue;
                    }

                    NameOrderField field;
                    if (TryGetNameOrderField(compact[index].ToString(), out field))
                    {
                        fields.Add(field);
                    }

                    index++;
                }
            }

            return new NameOrderAlternative(fields, requiresComma);
        }

        private static bool TryAddNameOrderWord(string word, List<NameOrderField> fields)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return false;
            }

            NameOrderField field;
            if (TryGetNameOrderField(word, out field))
            {
                fields.Add(field);
                return true;
            }

            if (word.Length > 1)
            {
                bool addedAny = false;
                for (int index = 0; index < word.Length;)
                {
                    if (index + 1 < word.Length &&
                        string.Equals(word.Substring(index, 2), "ID", StringComparison.OrdinalIgnoreCase))
                    {
                        fields.Add(NameOrderField.Identifier);
                        index += 2;
                        addedAny = true;
                        continue;
                    }

                    if (TryGetNameOrderField(word[index].ToString(), out field))
                    {
                        fields.Add(field);
                        addedAny = true;
                    }

                    index++;
                }

                return addedAny;
            }

            return false;
        }

        private static bool TryGetNameOrderField(string value, out NameOrderField field)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "I":
                case "ID":
                case "IDENTIFIER":
                case "PERSONID":
                case "PERSONIDENTIFIER":
                    field = NameOrderField.Identifier;
                    return true;
                case "T":
                case "TITLE":
                case "PREFIX":
                    field = NameOrderField.Title;
                    return true;
                case "F":
                case "FIRST":
                case "GIVEN":
                    field = NameOrderField.Given;
                    return true;
                case "M":
                case "MIDDLE":
                case "FURTHER":
                    field = NameOrderField.Middle;
                    return true;
                case "L":
                case "LAST":
                case "FAMILY":
                case "SURNAME":
                    field = NameOrderField.Family;
                    return true;
                default:
                    field = NameOrderField.Given;
                    return false;
            }
        }

        private static void ConsumeIdentifierField(
            List<string> tokens,
            List<NameOrderField> fields,
            bool allowIdentifier,
            bool identifierWasExtractedFromName,
            ref string identifier)
        {
            int idFieldIndex = fields.IndexOf(NameOrderField.Identifier);
            if (idFieldIndex < 0 || tokens.Count == 0)
            {
                return;
            }

            if (identifierWasExtractedFromName && !string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            int tokenIndex = idFieldIndex;
            if (tokenIndex >= tokens.Count)
            {
                tokenIndex = idFieldIndex == fields.Count - 1 ? tokens.Count - 1 : -1;
            }

            if (tokenIndex < 0)
            {
                return;
            }

            string parsedIdentifier = CleanIdentifierToken(tokens[tokenIndex]);
            tokens.RemoveAt(tokenIndex);
            if (allowIdentifier && string.IsNullOrWhiteSpace(identifier))
            {
                identifier = parsedIdentifier;
            }
        }

        private static bool TryBuildOrderedName(
            IReadOnlyList<string> tokens,
            IReadOnlyList<NameOrderField> fields,
            out Hl7NameComponents name)
        {
            name = null;
            if (tokens.Count == 0)
            {
                return false;
            }

            if (FieldsEqual(fields, NameOrderField.Given, NameOrderField.Middle, NameOrderField.Family))
            {
                name = new Hl7NameComponents(
                    string.Empty,
                    LastToken(tokens),
                    tokens[0],
                    JoinTokenRange(tokens, 1, tokens.Count - 2),
                    string.Empty,
                    string.Empty);
                return true;
            }

            if (FieldsEqual(fields, NameOrderField.Given, NameOrderField.Family))
            {
                name = new Hl7NameComponents(
                    string.Empty,
                    LastToken(tokens),
                    JoinTokenRange(tokens, 0, tokens.Count - 2),
                    string.Empty,
                    string.Empty,
                    string.Empty);
                return true;
            }

            if (FieldsEqual(fields, NameOrderField.Family, NameOrderField.Given, NameOrderField.Middle))
            {
                name = new Hl7NameComponents(
                    string.Empty,
                    tokens[0],
                    tokens.Count > 1 ? tokens[1] : string.Empty,
                    JoinTokenRange(tokens, 2, tokens.Count - 1),
                    string.Empty,
                    string.Empty);
                return true;
            }

            if (FieldsEqual(fields, NameOrderField.Family, NameOrderField.Given))
            {
                name = new Hl7NameComponents(
                    string.Empty,
                    tokens[0],
                    JoinTokenRange(tokens, 1, tokens.Count - 1),
                    string.Empty,
                    string.Empty,
                    string.Empty);
                return true;
            }

            return false;
        }

        private static bool FieldsEqual(IReadOnlyList<NameOrderField> fields, params NameOrderField[] expected)
        {
            return fields.Count == expected.Length && fields.SequenceEqual(expected);
        }

        private static Hl7NameComponents ParseNameWithDefaultOrder(string value, string identifier)
        {
            string working = NormalizeWhitespace(value);
            string prefix = ExtractLeadingNamePrefix(ref working);
            if (string.IsNullOrWhiteSpace(working))
            {
                return new Hl7NameComponents(identifier, string.Empty, string.Empty, string.Empty, string.Empty, prefix);
            }

            if (working.Contains(","))
            {
                string[] parts = working.Split(new[] { ',' }, 2, StringSplitOptions.None)
                    .Select(part => part.Trim())
                    .ToArray();
                string[] givenTokens = parts.Length > 1 ? SplitNameTokens(parts[1]) : Array.Empty<string>();

                return new Hl7NameComponents(
                    identifier,
                    parts[0],
                    givenTokens.Length > 0 ? givenTokens[0] : string.Empty,
                    JoinTokenRange(givenTokens, 1, givenTokens.Length - 1),
                    string.Empty,
                    prefix);
            }

            string[] tokens = SplitNameTokens(working);
            if (tokens.Length == 0)
            {
                return new Hl7NameComponents(identifier, string.Empty, string.Empty, string.Empty, string.Empty, prefix);
            }

            if (tokens.Length == 1)
            {
                return new Hl7NameComponents(identifier, tokens[0], string.Empty, string.Empty, string.Empty, prefix);
            }

            return new Hl7NameComponents(
                identifier,
                tokens[tokens.Length - 1],
                tokens[0],
                JoinTokenRange(tokens, 1, tokens.Length - 2),
                string.Empty,
                prefix);
        }

        private static string ExtractLeadingNamePrefix(ref string value)
        {
            string[] tokens = SplitWords(value);
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

            int firstTokenIndex = (value ?? string.Empty).IndexOf(tokens[0], StringComparison.Ordinal);
            value = firstTokenIndex < 0
                ? string.Empty
                : NormalizeWhitespace(value.Substring(firstTokenIndex + tokens[0].Length));
            return prefix;
        }

        private static string ExtractBracketedIdentifier(ref string value)
        {
            MatchCollection matches = BracketedIdentifierPattern.Matches(value ?? string.Empty);
            foreach (Match match in matches)
            {
                string identifier = match.Groups["identifier"].Value;
                if (!identifier.Any(char.IsDigit))
                {
                    continue;
                }

                value = NormalizeWhitespace((value ?? string.Empty).Remove(match.Index, match.Length));
                return identifier;
            }

            return string.Empty;
        }

        private static string ExtractLeadingNumericIdentifier(ref string value)
        {
            Match match = LeadingNumericIdentifierPattern.Match(value ?? string.Empty);
            if (!match.Success)
            {
                return string.Empty;
            }

            value = NormalizeWhitespace(match.Groups["name"].Value);
            return match.Groups["identifier"].Value.Trim();
        }

        private static string CleanIdentifierToken(string value)
        {
            return (value ?? string.Empty).Trim().Trim('(', ')');
        }

        private static string[] SplitNameTokens(string value)
        {
            return MergeBracketedNicknames(SplitWords((value ?? string.Empty).Replace(",", " ")));
        }

        private static string[] MergeBracketedNicknames(IReadOnlyList<string> tokens)
        {
            List<string> merged = new List<string>();
            foreach (string token in tokens ?? Array.Empty<string>())
            {
                if (merged.Count > 0 &&
                    token.StartsWith("(", StringComparison.Ordinal) &&
                    token.EndsWith(")", StringComparison.Ordinal) &&
                    !token.Any(char.IsDigit))
                {
                    merged[merged.Count - 1] = merged[merged.Count - 1] + " " + token;
                    continue;
                }

                merged.Add(token);
            }

            return merged.ToArray();
        }

        private static string[] SplitWords(string value)
        {
            return NormalizeWhitespace(value)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length > 0)
                .ToArray();
        }

        private static string NormalizeWhitespace(string value)
        {
            return RepeatedWhitespacePattern.Replace(value ?? string.Empty, " ").Trim();
        }

        private static string LastToken(IReadOnlyList<string> tokens)
        {
            return tokens.Count == 0 ? string.Empty : tokens[tokens.Count - 1];
        }

        private static string JoinTokenRange(IReadOnlyList<string> tokens, int startIndex, int endIndex)
        {
            if (tokens.Count == 0 || startIndex < 0 || endIndex < startIndex || startIndex >= tokens.Count)
            {
                return string.Empty;
            }

            int safeEndIndex = Math.Min(endIndex, tokens.Count - 1);
            return string.Join(" ", tokens.Skip(startIndex).Take(safeEndIndex - startIndex + 1).ToArray());
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

        private enum NameOrderField
        {
            Identifier,
            Title,
            Family,
            Given,
            Middle
        }

        private sealed class NameOrderAlternative
        {
            internal NameOrderAlternative(IReadOnlyList<NameOrderField> fields, bool requiresComma)
            {
                Fields = fields ?? Array.Empty<NameOrderField>();
                RequiresComma = requiresComma;
            }

            internal IReadOnlyList<NameOrderField> Fields { get; }
            internal bool RequiresComma { get; }
        }

        private sealed class Hl7NameComponents
        {
            internal Hl7NameComponents(
                string identifier,
                string familyName,
                string givenName,
                string furtherGivenNames,
                string suffix,
                string prefix)
            {
                Identifier = identifier ?? string.Empty;
                FamilyName = familyName ?? string.Empty;
                GivenName = givenName ?? string.Empty;
                FurtherGivenNames = furtherGivenNames ?? string.Empty;
                Suffix = suffix ?? string.Empty;
                Prefix = prefix ?? string.Empty;
            }

            internal string Identifier { get; }
            internal string FamilyName { get; }
            internal string GivenName { get; }
            internal string FurtherGivenNames { get; }
            internal string Suffix { get; }
            internal string Prefix { get; }
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
