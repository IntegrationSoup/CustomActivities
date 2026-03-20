using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AmazonActivities
{
    internal abstract class AwsActivityBase : CustomActivity
    {
        protected static AwsActivitySupport.S3UploadRequest CreateRequest(Dictionary<string, string> parameters)
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

        protected static void EnsureActivityInstanceReady(IActivityInstance activityInstance)
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

        private static string GetRequiredParameter(Dictionary<string, string> parameters, string name)
        {
            if (parameters.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            throw new ArgumentException($"Parameter '{name}' is required.");
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
                throw new InvalidOperationException(
                    "The AWS runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

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
