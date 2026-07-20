using HL7Soup.Integrations;
using System;
using System.Collections.Generic;

namespace ValidateTransformer
{
    internal static class ValidationActivity
    {
        internal static string GetRequiredProfile(Dictionary<string, string> parameters)
        {
            string profile;
            if (parameters == null ||
                !parameters.TryGetValue("Profile", out profile) ||
                string.IsNullOrWhiteSpace(profile))
            {
                throw new Exception("A validation Profile is required.");
            }

            return profile.Trim();
        }

        internal static IHL7Message GetRequiredInputMessage(IActivityInstance activityInstance, string activityName)
        {
            IHL7Message hl7Message = activityInstance == null ? null : activityInstance.Message as IHL7Message;
            if (hl7Message == null)
            {
                throw new Exception(activityName + " requires an HL7 input message.");
            }

            return hl7Message;
        }

        internal static ValidationOutcome Validate(IHL7Message hl7Message, string profile)
        {
            string rawOutcome = ValidationProfileCache.ValidateWithLatestProfile(hl7Message, profile);
            return ValidationOutcomeBuilder.Build(rawOutcome, profile);
        }
    }
}
