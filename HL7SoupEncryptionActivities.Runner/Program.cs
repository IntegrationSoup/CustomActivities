using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace HL7SoupEncryptionActivities.Runner
{
    internal static class Program
    {
        private static readonly byte[] Salt = { 0x51, 0x69, 0x52, 0x7e, 0x20, 0x4d, 0x65, 0x94, 0x46, 0x65, 0x74, 0x65, 0x76 };

        private static int Main(string[] args)
        {
            EncryptionResponse response = null;

            try
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: EncryptionActivitiesRunner <requestPath> <responsePath>");
                    return 2;
                }

                string requestPath = Path.GetFullPath(args[0]);
                string responsePath = Path.GetFullPath(args[1]);
                if (!File.Exists(requestPath))
                {
                    Console.Error.WriteLine($"Request file was not found: {requestPath}");
                    return 3;
                }

                EncryptionRequest request = DeserializeJson<EncryptionRequest>(requestPath);
                response = Execute(request);
                WriteResponse(responsePath, response);
                return response.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                response = response ?? new EncryptionResponse
                {
                    Success = false,
                    Message = ex.Message
                };

                if (args.Length >= 2)
                {
                    try
                    {
                        WriteResponse(Path.GetFullPath(args[1]), response);
                    }
                    catch
                    {
                        // Best-effort only.
                    }
                }

                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static EncryptionResponse Execute(EncryptionRequest request)
        {
            ValidateRequest(request);

            string inputText = File.ReadAllText(request.InputPath, Encoding.UTF8);
            string outputText;

            switch ((request.Operation ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "encrypt":
                    outputText = Encrypt(inputText, request.EncryptionKey);
                    break;
                case "decrypt":
                    outputText = Decrypt(inputText, request.EncryptionKey);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported encryption operation '{request.Operation}'.");
            }

            File.WriteAllText(request.OutputPath, outputText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new EncryptionResponse
            {
                Success = true,
                Message = "Completed"
            };
        }

        private static string Encrypt(string contentToEncrypt, string encryptionKey)
        {
            byte[] clearBytes = Encoding.Unicode.GetBytes(contentToEncrypt ?? string.Empty);
            using (Aes encryptor = Aes.Create())
            {
                Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(encryptionKey, Salt);
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(clearBytes, 0, clearBytes.Length);
                        cs.Close();
                    }

                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        private static string Decrypt(string encryptedText, string encryptionKey)
        {
            byte[] cipherBytes = Convert.FromBase64String(encryptedText ?? string.Empty);
            using (Aes encryptor = Aes.Create())
            {
                Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(encryptionKey, Salt);
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(cipherBytes, 0, cipherBytes.Length);
                        cs.Close();
                    }

                    return Encoding.Unicode.GetString(ms.ToArray());
                }
            }
        }

        private static void ValidateRequest(EncryptionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Operation))
            {
                throw new InvalidOperationException("Operation is required.");
            }

            if (string.IsNullOrWhiteSpace(request.EncryptionKey))
            {
                throw new InvalidOperationException("Encryption Key is required.");
            }

            if (string.IsNullOrWhiteSpace(request.InputPath) || !File.Exists(request.InputPath))
            {
                throw new FileNotFoundException("The input file could not be found.", request?.InputPath);
            }

            if (string.IsNullOrWhiteSpace(request.OutputPath))
            {
                throw new InvalidOperationException("OutputPath is required.");
            }
        }

        private static void WriteResponse(string responsePath, EncryptionResponse response)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(responsePath) ?? AppDomain.CurrentDomain.BaseDirectory);
            SerializeJson(responsePath, response);
        }

        private static void SerializeJson<T>(string path, T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (FileStream stream = File.Create(path))
            {
                serializer.WriteObject(stream, value);
            }
        }

        private static T DeserializeJson<T>(string path)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (FileStream stream = File.OpenRead(path))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        [DataContract]
        private sealed class EncryptionRequest
        {
            [DataMember(Order = 1)]
            public string Operation { get; set; }

            [DataMember(Order = 2)]
            public string EncryptionKey { get; set; }

            [DataMember(Order = 3)]
            public string InputPath { get; set; }

            [DataMember(Order = 4)]
            public string OutputPath { get; set; }
        }

        [DataContract]
        private sealed class EncryptionResponse
        {
            [DataMember(Order = 1)]
            public bool Success { get; set; }

            [DataMember(Order = 2)]
            public string Message { get; set; }
        }
    }
}
