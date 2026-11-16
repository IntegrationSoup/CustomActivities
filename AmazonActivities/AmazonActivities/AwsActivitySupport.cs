#if EXTENSION_BRIDGE_RUNNER
using Amazon.Runtime;
#endif
using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
#if EXTENSION_BRIDGE_RUNNER
using System.Net;
#endif
using System.Runtime.Serialization;
using System.Text;

namespace AmazonActivities
{
    public abstract class AwsActivityBase : CustomActivity
    {
        private protected static AwsActivitySupport.S3UploadRequest CreateRequest(Dictionary<string, string> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            return new AwsActivitySupport.S3UploadRequest
            {
                BucketName = GetRequiredParameter(parameters, "Bucket Name"),
                FileName = GetRequiredParameter(parameters, "File Name"),
                Region = GetRequiredParameter(parameters, "Region"),
                AccessKeyId = GetRequiredParameter(parameters, "Access Key ID"),
                SecretAccessKey = GetRequiredParameter(parameters, "Secret Access Key")
            };
        }

        private protected static void EnsureActivityInstanceReady(IActivityInstance activityInstance)
        {
            if (activityInstance == null)
            {
                throw new ArgumentNullException(nameof(activityInstance));
            }

            if (activityInstance.Message == null)
            {
                throw new InvalidOperationException("Error: Activity Message not set.");
            }

            if (activityInstance.ResponseMessage == null)
            {
                throw new InvalidOperationException("Error: Response Message not set.");
            }
        }

        private protected static byte[] GetMessageBytes(IMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (message is IDicomMessage dicomMessage)
            {
                string base64Dicom = dicomMessage.GetBase64EncodedDicom();
                return string.IsNullOrEmpty(base64Dicom)
                    ? Array.Empty<byte>()
                    : Convert.FromBase64String(base64Dicom);
            }

            string text = message.Text ?? string.Empty;
            if (IsBinaryMessage(message))
            {
                try
                {
                    return Convert.FromBase64String(text);
                }
                catch (FormatException)
                {
                    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
                }
            }

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        }

        private static bool IsBinaryMessage(IMessage message)
        {
            if (message == null)
            {
                return false;
            }

            Type messageType = message.GetType();
            if (string.Equals(messageType.Name, "BinaryMessage", StringComparison.Ordinal))
            {
                return true;
            }

            object reflectedMessageType = messageType.GetProperty("MessageType")?.GetValue(message);
            return string.Equals(reflectedMessageType?.ToString(), "Binary", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRequiredParameter(Dictionary<string, string> parameters, string name)
        {
            if (parameters.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

#if EXTENSION_BRIDGE_RUNNER
            throw new ExtensionReasonException($"The parameter '{name}' is required.");
#else
            throw new ArgumentException($"Parameter '{name}' is required.");
#endif
        }
    }

    internal static class AwsActivitySupport
    {
        private const string RunnerPathEnvironmentVariable = "AWSACTIVITIES_RUNNER_PATH";
        private const string RunnerDirectoryName = "AwsActivitiesRunner";
        private const string RunnerExecutableName = "AwsActivitiesRunner.exe";
        private const int RunnerTimeoutMilliseconds = 180000;

        private static readonly PersistentRunnerClient runnerClient = new PersistentRunnerClient("AWS runner");

        internal static S3UploadResponse Upload(S3UploadRequest request, byte[] inputBytes)
        {
            string runnerExecutablePath = ResolveRunnerExecutablePath();

            try
            {
                S3UploadPipeResponse response = runnerClient.Invoke<S3UploadPipeRequest, S3UploadPipeResponse>(
                    runnerExecutablePath,
                    "upload-s3",
                    new S3UploadPipeRequest
                    {
                        BucketName = request.BucketName,
                        FileName = request.FileName,
                        Region = request.Region,
                        AccessKeyId = request.AccessKeyId,
                        SecretAccessKey = request.SecretAccessKey,
                        InputBase64 = Convert.ToBase64String(inputBytes ?? Array.Empty<byte>())
                    },
                    RunnerTimeoutMilliseconds);

                return new S3UploadResponse
                {
                    Success = true,
                    Message = response?.Message ?? "Code Execute Successfully",
                    ObjectPath = response?.ObjectPath ?? string.Empty
                };
            }
            catch (Exception ex)
            {
#if EXTENSION_BRIDGE_RUNNER
                string reason = DescribeKnownFailure(request, ex);
                if (reason != null)
                {
                    throw new ExtensionReasonException(reason);
                }
#endif
                throw new InvalidOperationException(
                    "The AWS runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

#if EXTENSION_BRIDGE_RUNNER
        // Version 5 runs this library through the extension bridge, which shows the Host only a
        // sentence the library wrote on purpose. S3's refusals are named here from structured facts
        // alone (the AWS error code and HTTP status), never from the SDK's or .NET's message text,
        // and the sentence holds only what the person configured. Anything else stays masked.
        // PermanentRedirect and AuthorizationHeaderMalformed are left masked on purpose: the SDK
        // treats them as a sign of the wrong region, but S3 also answers AuthorizationHeaderMalformed
        // for other malformed credentials, so a region sentence could be wrong.
        private static string DescribeKnownFailure(S3UploadRequest request, Exception exception)
        {
            AmazonServiceException service = FindServiceException(exception, 0);
            if (service == null)
            {
                return null;
            }

            switch (service.ErrorCode)
            {
                case "NoSuchBucket" when service.StatusCode == HttpStatusCode.NotFound:
                    return $"The S3 bucket '{request.BucketName}' was not found.";
                case "InvalidAccessKeyId" when service.StatusCode == HttpStatusCode.Forbidden:
                    return "AWS did not recognise the Access Key ID.";
                case "SignatureDoesNotMatch" when service.StatusCode == HttpStatusCode.Forbidden:
                    return "AWS refused the Secret Access Key for this Access Key ID.";
                case "AccessDenied" when service.StatusCode == HttpStatusCode.Forbidden:
                    return $"AWS refused access to the S3 bucket '{request.BucketName}' for this Access Key ID.";
                default:
                    return null;
            }
        }

        // TransferUtility may wrap the SDK's exception, for example in an AggregateException.
        private static AmazonServiceException FindServiceException(Exception exception, int depth)
        {
            if (exception == null || depth > 8)
            {
                return null;
            }

            if (exception is AmazonServiceException service)
            {
                return service;
            }

            if (exception is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    AmazonServiceException found = FindServiceException(inner, depth + 1);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return FindServiceException(exception.InnerException, depth + 1);
        }
#endif

        private static string ResolveRunnerExecutablePath()
        {
            return PersistentRunnerExecutableResolver.ResolveExecutablePath(
                typeof(S3Sender),
                RunnerPathEnvironmentVariable,
                RunnerDirectoryName,
                RunnerExecutableName,
                "AWS runner");
        }

        internal sealed class S3UploadRequest
        {
            internal string BucketName { get; set; }
            internal string FileName { get; set; }
            internal string Region { get; set; }
            internal string AccessKeyId { get; set; }
            internal string SecretAccessKey { get; set; }
        }

        internal sealed class S3UploadResponse
        {
            internal bool Success { get; set; }
            internal string Message { get; set; }
            internal string ObjectPath { get; set; }
        }

        [DataContract]
        private sealed class S3UploadPipeRequest
        {
            [DataMember(Order = 1)]
            public string BucketName { get; set; }

            [DataMember(Order = 2)]
            public string FileName { get; set; }

            [DataMember(Order = 3)]
            public string Region { get; set; }

            [DataMember(Order = 4)]
            public string AccessKeyId { get; set; }

            [DataMember(Order = 5)]
            public string SecretAccessKey { get; set; }

            [DataMember(Order = 6)]
            public string InputBase64 { get; set; }
        }

        [DataContract]
        private sealed class S3UploadPipeResponse
        {
            [DataMember(Order = 1)]
            public string Message { get; set; }

            [DataMember(Order = 2)]
            public string ObjectPath { get; set; }
        }
    }
}
