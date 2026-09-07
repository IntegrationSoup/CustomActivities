using System;
using Popokey.ExtensionRunners;
using HL7ValueTransformers;

namespace HL7ValueTransformers.Runner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                int? result = BridgeRunner.RunIfRequested(args, UnsupportedLegacyOperation, () => new BridgeProvider(
                    "popokey.hl7valuetransformers", "HL7ValueTransformers", UnsupportedLegacyOperation,
                    typeof(DigitsOnlyTransformer), typeof(LettersAndDigitsOnlyTransformer), typeof(LettersDigitsAndSpacesOnlyTransformer),
                    typeof(TextToHl7NameTransformer), typeof(TextToHl7ClinicianNameTransformer), typeof(TextToHl7AddressTransformer), typeof(TextToHl7PhoneTransformer)));
                if (result.HasValue) return result.Value;
                Console.Error.WriteLine("Usage: HL7ValueTransformersRunner --server --pipe-name <name> --parent-pid <pid> --bridge-version 1");
                return 2;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
        }
        private static string UnsupportedLegacyOperation(string operation, string payload)
        {
            throw new InvalidOperationException("HL7 Value Transformers had no legacy runner protocol. Use the version 4 DLL or schema 1 bridge.");
        }
    }
}
