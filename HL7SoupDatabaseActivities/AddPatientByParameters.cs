using System;
using System.Collections.Generic;
using HL7Soup.Integrations;
using System.ComponentModel;
using System.Data.SqlClient;

namespace HL7SoupDatabaseActivities
{
    [DisplayName("Add Patient by Parameters")]
    [Parameter("ExternalPatientID", "The ID of the inbound patient", isRequired: true)]
    [Parameter("FirstName", "The first name of the patient")]
    [Parameter("LastName", "The last name of the patient", isRequired: true)]
    [Parameter("DateOfBirth", "The patients birth date")]
    [Variable("Patient ID", "1000")]
    public class AddPatientByParameters : CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            IHL7Message hl7Message = activityInstance.Message as IHL7Message;
            
            //Get the values from the HL7
            string externalPatientId = parameters["ExternalPatientID"];
            string firstName = parameters["FirstName"];
            string lastName = parameters["LastName"];
            DateTime birthDate = HL7Helpers.GetDateFromHL7Date(parameters["DateOfBirth"]);

            int patientId = 0; //returned patient ID from my database
            
            //if the patient ID is valid
            if (!string.IsNullOrEmpty(externalPatientId))
            {
                string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["HL7SoupDatabaseActivities.Properties.Settings.MedicalDB"].ConnectionString;

                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText =
@"IF NOT EXISTS(SELECT PatientID from Patient where ExternalPatientId = @externalPatiendId)
    INSERT INTO Patient (FirstName, LastName, Birthdate, ExternalPatientId) 
    OUTPUT Inserted.PatientID
    VALUES(@firstName, @lastName, @birthDate, @externalPatiendId)
ELSE
    UPDATE Patient 
    SET FirstName = @firstName
    ,LastName = @lastName
    ,Birthdate = @birthDate
    ,ExternalPatientId = @externalPatiendId
    OUTPUT Inserted.PatientID
    WHERE ExternalPatientId = @externalPatiendId";

                    command.Parameters.AddWithValue("@firstName", firstName);
                    command.Parameters.AddWithValue("@lastName", lastName);
                    command.Parameters.AddWithValue("@birthDate", birthDate);
                    command.Parameters.AddWithValue("@externalPatiendId", externalPatientId);

                    connection.Open();

                    patientId = (int)command.ExecuteScalar();

                    connection.Close();
                }

                //Set the HL7 Soup Variable 
                workflowInstance.SetVariable("Patient ID", patientId.ToString());
            }
        }


    }
}
