using HL7Soup.Integrations;
using System;
using System.Collections.Generic;

namespace ValidateTransformer
{
    internal static class ValidationActivity
    {
        internal const string ErrorIfInvalidParameterName = "Error if invalid";

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

        internal static bool GetErrorIfInvalid(Dictionary<string, string> parameters)
        {
            string value;
            if (parameters == null ||
                !parameters.TryGetValue(ErrorIfInvalidParameterName, out value) ||
                string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "1":
                case "on":
                    return true;

                case "false":
                case "no":
                case "0":
                case "off":
                    return false;

                default:
                    throw new Exception(ErrorIfInvalidParameterName + " must be true or false.");
            }
        }

        internal static IHL7Message GetRequiredInputMessage(
            IWorkflowInstance workflowInstance,
            IActivityInstance activityInstance,
            string activityName,
            out IDisposable ownedMessage)
        {
            ownedMessage = null;

            IMessage activityMessage = activityInstance == null ? null : activityInstance.Message;
            IHL7Message hl7Message = activityMessage as IHL7Message;
            if (hl7Message != null)
            {
                return hl7Message;
            }

            // In Integration Suite v4 the InMessage attribute controls the editor
            // type, but does not guarantee the runtime message type. Preserve an
            // explicitly bound/transformed activity message by reparsing its text.
            if (activityMessage != null && !string.IsNullOrWhiteSpace(activityMessage.Text))
            {
                return CreateOwnedHl7Message(
                    workflowInstance,
                    activityMessage.Text,
                    activityName,
                    "activity input",
                    out ownedMessage);
            }

            // A new custom activity can have an empty input template. In that case
            // Activity.ProcessActivity deliberately supplies a null current message,
            // while the receiver's original HL7 message remains available here.
            IMessage receivingMessage = workflowInstance == null ||
                                        workflowInstance.ReceivingActivityInstance == null
                ? null
                : workflowInstance.ReceivingActivityInstance.Message;
            hl7Message = receivingMessage as IHL7Message;
            if (hl7Message != null)
            {
                return hl7Message;
            }

            if (receivingMessage != null && !string.IsNullOrWhiteSpace(receivingMessage.Text))
            {
                return CreateOwnedHl7Message(
                    workflowInstance,
                    receivingMessage.Text,
                    activityName,
                    "received workflow input",
                    out ownedMessage);
            }

            throw new Exception(
                activityName +
                " requires an HL7 input message. Bind an HL7 message to the activity or run it from an HL7 receiver.");
        }

        private static IHL7Message CreateOwnedHl7Message(
            IWorkflowInstance workflowInstance,
            string text,
            string activityName,
            string source,
            out IDisposable ownedMessage)
        {
            ownedMessage = null;
            if (workflowInstance == null)
            {
                throw new Exception(activityName + " cannot parse the " + source + " without a workflow instance.");
            }

            IMessage parsedMessage = workflowInstance.CreateMessage(
                HL7Soup.BaseTypes.MessageTypes.HL7V2,
                text);
            IHL7Message hl7Message = parsedMessage as IHL7Message;
            if (hl7Message == null)
            {
                if (parsedMessage != null)
                {
                    parsedMessage.Dispose();
                }

                throw new Exception(activityName + " could not parse the " + source + " as HL7.");
            }

            ownedMessage = parsedMessage;
            return hl7Message;
        }

        internal static ValidationOutcome Validate(IHL7Message hl7Message, string profile)
        {
            string rawOutcome = ValidationProfileCache.ValidateWithLatestProfile(hl7Message, profile);
            return ValidationOutcomeBuilder.Build(rawOutcome, profile);
        }

        internal static void HandleInvalidOutcome(
            IWorkflowInstance workflowInstance,
            IMessage responseMessage,
            ValidationOutcome outcome,
            bool errorIfInvalid,
            bool promoteResponse)
        {
            if (!errorIfInvalid || outcome == null || !outcome.HasErrors)
            {
                return;
            }

            // A custom-activity exception escapes the v4 activity wrapper and prevents
            // the MLLP receiver from reaching its response-write block. Handle an
            // invalid validation outcome as a per-message workflow error instead.
            if (promoteResponse)
            {
                WorkflowErrorBridge.TryPromoteResponse(workflowInstance, responseMessage);
            }

            WorkflowErrorBridge.TryMarkErrored(workflowInstance, BuildErrorMessage(outcome));
        }

        internal static string BuildErrorMessage(ValidationOutcome outcome)
        {
            string message = "HL7 validation failed";
            if (outcome != null && !string.IsNullOrWhiteSpace(outcome.Profile))
            {
                message += " for profile '" + outcome.Profile + "'";
            }

            if (outcome != null && outcome.Errors != null && outcome.Errors.Count > 0)
            {
                ValidationError firstError = outcome.Errors[0];
                string path = firstError == null ? string.Empty : firstError.Path ?? string.Empty;
                string reason = firstError == null ? string.Empty : firstError.Reason ?? string.Empty;
                string firstErrorText = string.IsNullOrWhiteSpace(path)
                    ? reason
                    : string.IsNullOrWhiteSpace(reason) ? path : path + ": " + reason;

                if (!string.IsNullOrWhiteSpace(firstErrorText))
                {
                    message += ". " + firstErrorText;
                }
            }

            return message.TrimEnd('.') + ".";
        }
    }
}
