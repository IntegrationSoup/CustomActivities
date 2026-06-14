using System;
using System.Collections.Generic;
using HL7Soup.Integrations;
using System.ComponentModel;
using System.Data.SqlClient;

namespace HL7SoupDatabaseActivities
{
    [DisplayName("Add Patient by Message")]
    [InMessage(@"MSH|^~\&|HL7Soup|Instance1|HL7Soup|Instance2|20010520173800| |SIU^S12|93710600|P|2.5.1|AL||
SCH|Placer001|Filler001|||||^Unklare Beschwerden||||^^^20010701100000^20010701103000
PID|1||001000||TEST^TEST^TEST^^^TEST|Schulz|19670808|M|||||(089) 14002243|(089) 1234|||||198708080150|||||||||D|20020131063", TypeOfMessages.HL7)]
    [OutMessage(@"PatientID,FirstName,LastName,BirthDate", TypeOfMessages.CSV)]
    public class AddPatientByMessage : CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            IHL7Message hl7Message = activityInstance.Message as IHL7Message;
            
            //Get the values from the HL7
            string externalPatientId = hl7Message.GetValueAtPath("PID-3.1");
            string firstName = hl7Message.GetValueAtPath("PID-5.2");
            string lastName = hl7Message.GetValueAtPath("PID-5.1");
            DateTime birthDate = HL7Helpers.GetDateFromHL7Date(hl7Message.GetValueAtPath("PID-7"));

            int patientId = 0; //returned patient ID from my database

            //if the patient ID is valid
            if (!string.IsNullOrEmpty(externalPatientId))
            {
                string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["HL7SoupDatabaseActivities.Properties.Settings.MedicalDB"].ConnectionString;

                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText =
@"IF NOT EXISTS(SELECT PatientID from Patient where ExternalPatientId = @externalPatientId)
    INSERT INTO Patient (FirstName, LastName, Birthdate, ExternalPatientId) 
    OUTPUT Inserted.PatientID
    VALUES(@firstName, @lastName, @birthDate, @externalPatientId)
ELSE
    UPDATE Patient 
    SET FirstName = @firstName
    ,LastName = @lastName
    ,Birthdate = @birthDate
    ,ExternalPatientId = @externalPatientId
    OUTPUT Inserted.PatientID
    WHERE ExternalPatientId = @externalPatientId";

                    command.Parameters.AddWithValue("@firstName", firstName);
                    command.Parameters.AddWithValue("@lastName", lastName);
                    command.Parameters.AddWithValue("@birthDate", birthDate);
                    command.Parameters.AddWithValue("@externalPatientId", externalPatientId);

                    connection.Open();

                    patientId = (int)command.ExecuteScalar();

                    connection.Close();
                }

                //Set the HL7 Soup Variable 
                activityInstance.ResponseMessage.SetValueAtPath("[0]", patientId.ToString());
                activityInstance.ResponseMessage.SetValueAtPath("[1]", firstName);
                activityInstance.ResponseMessage.SetValueAtPath("[2]", lastName);
                activityInstance.ResponseMessage.SetValueAtPath("[3]", HL7Helpers.GetHL7Date(birthDate));
            }
        }


    }
}
