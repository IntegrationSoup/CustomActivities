using System;
using System.Collections.Generic;
using HL7Soup.Integrations;
using System.ComponentModel;
using System.Data.SqlClient;

namespace HL7SoupDatabaseActivities
{
    [DisplayName("Add Appointment")]
    [Parameter("Patient ID", "The ID of the patient", isRequired: true)]
    [Parameter("Appointment ID", "The ID of the appointment", isRequired: true)]
    [Parameter("Start Date", "The start date of the appointment", isRequired: true)]
    [Parameter("End Date", "The start date of the appointment", isRequired: true)]
    public class AddAppointment : CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            //Get the values from the HL7
            string patientId = parameters["Patient ID"];
            string appointmentId = parameters["Appointment ID"];
            DateTime startDate = HL7Helpers.GetDateFromHL7Date(parameters["Start Date"]);
            DateTime endDate = HL7Helpers.GetDateFromHL7Date(parameters["End Date"]);

            if (string.IsNullOrEmpty(patientId))
                throw new ArgumentException("A valid patient ID is required.");

            if (string.IsNullOrEmpty(appointmentId))
                throw new ArgumentException("A valid appointment ID is required.");

            if (startDate == DateTime.MinValue)
                throw new ArgumentException("A valid start date is required.");

            if (endDate == DateTime.MinValue)
                throw new ArgumentException("A valid end date is required.");


            {
                string connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["HL7SoupDatabaseActivities.Properties.Settings.MedicalDB"].ConnectionString;

                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText =
@"IF NOT EXISTS(SELECT AppointmentId from Appointment where AppointmentId = @appointmentId)
    INSERT INTO Appointment (patientId, appointmentId, startDate, endDate) 
    VALUES(@patientId, @appointmentId, @startDate, @endDate)
ELSE
    UPDATE Appointment 
    SET patientId = @patientId
    ,appointmentId = @appointmentId
    ,startDate = @startDate
    ,endDate = @endDate
    WHERE AppointmentId = @appointmentId";

                    command.Parameters.AddWithValue("@patientId", patientId);
                    command.Parameters.AddWithValue("@appointmentId", appointmentId);
                    command.Parameters.AddWithValue("@startDate", startDate);
                    command.Parameters.AddWithValue("@endDate", endDate);

                    connection.Open();

                    command.ExecuteNonQuery();

                    connection.Close();
                }
            }
        }
    }
}
