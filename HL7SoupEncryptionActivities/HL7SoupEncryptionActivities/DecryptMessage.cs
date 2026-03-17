using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;

namespace HL7SoupEncryptionActivities
{
    [DisplayName("Decrypt Message")]
    [Parameter("Encryption Key", "The key used to encrypt and decrypt the message>", isRequired: true)]
    [InMessage(@"", TypeOfMessages.Text)]
    [OutMessage(@"Decrypted Text", TypeOfMessages.Text)]
    internal class DecryptMessage : CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            string EncryptionKey = parameters["Encryption Key"];
            string encryptedText = activityInstance.Message.Text;
            byte[] cipherBytes = Convert.FromBase64String(encryptedText);
            using (Aes encryptor = Aes.Create())
            {
                Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(EncryptionKey, new byte[] { 0x51, 0x69, 0x52, 0x7e, 0x20, 0x4d, 0x65, 0x94, 0x46, 0x65, 0x74, 0x65, 0x76 });
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(cipherBytes, 0, cipherBytes.Length);
                        cs.Close();
                    }
                    var decryptedText = Encoding.Unicode.GetString(ms.ToArray());
                    activityInstance.ResponseMessage.SetValueAtPath("", decryptedText);
                }
            }
        }
    }
}
