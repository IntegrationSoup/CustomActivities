using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;

namespace HL7SoupEncryptionActivities
{
    [DisplayName("Encrypt Message")]
    [Parameter("Encryption Key", "The key used to encrypt and decrypt the message. Provide a good key of at least 12 characters.", isRequired: true)]
    [InMessage(@"", TypeOfMessages.HL7)]
    [OutMessage(@"Encrypted Text", TypeOfMessages.Text)]
    internal class EncryptMessage : CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            string EncryptionKey = parameters["Encryption Key"];
            string contentToEncrypt = activityInstance.Message.Text;
            byte[] clearBytes = Encoding.Unicode.GetBytes(contentToEncrypt);
            using (Aes encryptor = Aes.Create())
            {
                Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(EncryptionKey, new byte[] { 0x51, 0x69, 0x52, 0x7e, 0x20, 0x4d, 0x65, 0x94, 0x46, 0x65, 0x74, 0x65, 0x76 });
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(clearBytes, 0, clearBytes.Length);
                        cs.Close();
                    }
                    var encrypted = Convert.ToBase64String(ms.ToArray());
                    activityInstance.ResponseMessage.SetValueAtPath("", encrypted);
                }
            }
        }
    }
}
