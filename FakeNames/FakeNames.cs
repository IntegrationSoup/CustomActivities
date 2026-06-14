using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HL7Soup.FakeNames
{
    public class FakeNames
    {
        private static volatile FakeNames instance;
        private static object syncRoot = new Object();

        private FakeNames() { }

        public static FakeNames Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                            instance = new FakeNames();
                        instance.EnsureDirectoryExists();
                        instance.LookupTablesDictionary = new Dictionary<string, Person>(StringComparer.InvariantCultureIgnoreCase);
                    }
                }

                return instance;
            }
        }

        private Dictionary<string, Person> LookupTablesDictionary { get; set; }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(settingPath))
            {
                Directory.CreateDirectory(settingPath);
            }
        }

        private static string ProgramDataPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        //private string settingPath = Path.Combine(HL7Soup.Functions.EnvironmentFunctions.Instance.ProgramDataPath, $@"Popokey{Path.DirectorySeparatorChar}LookupTables");

        private string settingPath = Path.Combine(ProgramDataPath, $@"Popokey{Path.DirectorySeparatorChar}DataTables");

        //public static string settingPath = @"C:\Users\Public\Documents\HL7Soup\Settings\FakeNames";
        private void LoadLookupTable(string lookupTableName)
        {
            string path = GetLookupTableFilePath(lookupTableName);

            if (!File.Exists(path))
            {
                throw new Exception($"Fake names file '{lookupTableName}' does not exist.  Add with the UI or add it as a two column CSV here. {path}");
            }

            Person lookupTable = new Person();

            foreach (string line in File.ReadAllLines(path))
            {
                if (!string.IsNullOrEmpty(line))
                {
                    System.Text.RegularExpressions.MatchCollection matches =
                                    new System.Text.RegularExpressions.Regex("((?<=\")[^\"]*(?=\"(,|$)+)|(?<=,|^)[^,\"]*(?=,|$))").Matches(line);
                    if (matches.Count > 1)
                    {
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            if (match.Value.Contains(","))
                            {
                                throw new Exception("Error: csv row may not contain the comma character");
                            }
                        } 
                        lookupTable.Table[matches[0].ToString()] = matches[1].ToString();
                    }
                }
            }

            LookupTablesDictionary[lookupTableName] = lookupTable;
        }

        public static string[] ParseCsvRow(string csvrow)
        {
            const string obscureCharacter = "ᖳ";
            if (csvrow.Contains(obscureCharacter))
                throw new Exception("Error: csv row may not contain the " + obscureCharacter + " character");

            var unicodeSeparatedString = "";

            var quotesArray = csvrow.Split('"');  // Split string on double quote character
            if (quotesArray.Length > 1)
            {
                for (var i = 0; i < quotesArray.Length; i++)
                {
                    // CSV must use double quotes to represent a quote inside a quoted cell
                    // Quotes must be paired up
                    // Test if a comma lays outside a pair of quotes.  If so, replace the comma with an obscure unicode character
                    if (Math.Round(Math.Round((decimal)i / 2) * 2) == i)
                    {
                        var s = quotesArray[i].Trim();
                        switch (s)
                        {
                            case ",":
                                quotesArray[i] = obscureCharacter;  // Change quoted comma seperated string to quoted "obscure character" seperated string
                                break;
                        }
                    }
                    // Build string and Replace quotes where quotes were expected.
                    unicodeSeparatedString += (i > 0 ? "\"" : "") + quotesArray[i].Trim();
                }
            }
            else
            {
                // String does not have any pairs of double quotes.  It should be safe to just replace the commas with the obscure character
                unicodeSeparatedString = csvrow.Replace(",", obscureCharacter);
            }

            var csvRowArray = unicodeSeparatedString.Split(obscureCharacter[0]);

            for (var i = 0; i < csvRowArray.Length; i++)
            {
                var s = csvRowArray[i].Trim();
                if (s.StartsWith("\"", StringComparison.Ordinal) && s.EndsWith("\"", StringComparison.Ordinal))
                {
                    csvRowArray[i] = s.Length > 2 ? s.Substring(1, s.Length - 2) : "";  // Remove start and end quotes.
                }
            }

            return csvRowArray;
        }

        private string GetLookupTableFilePath(string lookupTableName)
        {
            return Path.Combine(settingPath, Helpers.ReplaceInvalidFileNameChars(lookupTableName) + ".csv");
        }

        public static string ReplaceInvalidFileNameChars(this string s, string replacement = "")
        {
            return Regex.Replace(s,
              "[" + Regex.Escape(new String(System.IO.Path.GetInvalidFileNameChars())) + "]",
              replacement, //can even use a replacement string of any length
              RegexOptions.IgnoreCase);
            //not using System.IO.Path.InvalidPathChars (deprecated insecure API)
        }
    }
}
