using System;
using System.Collections.Generic;
using HL7Soup.Integrations;
using System.ComponentModel;
using System.Data.SqlClient;

namespace HL7SoupDatabaseActivities
{
    [DisplayName("Test Activity")]
    [InMessage(@"MSH|^~\&|HL7Soup|Instance1|HL7Soup|Instance2|20010520173800| |SIU^S12|93710600|P|2.5.1|AL||
SCH|Placer001|Filler001|||||^Unklare Beschwerden||||^^^20010701100000^20010701103000
PID|1||001000||TEST^TEST^TEST^^^TEST|Schulz|19670808|M|||||(089) 14002243|(089) 1234|||||198708080150|||||||||D|20020131063", TypeOfMessages.HL7)]
    [OutMessage(@"MSH|^~\&|ADT|GLN|OACIS|DHS|201810081136||ADT^A08^ADT_A08|292073|D|2.3.1|292073||AL|NE|AUS
EVN|A08|20161006060000||AUP\S\Admission Details Updated|jangel01^Angeli^Joe|201809141205
PID|||192872^^^GLN^MR~9999999999^9^M10^AUSHIC^MC", TypeOfMessages.HL7)]

    public class TestActivity : CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            IHL7Message hl7Message = activityInstance.Message as IHL7Message;

            IHL7Message responseMessage = activityInstance.ResponseMessage as IHL7Message;

            responseMessage.SetStructureAtPath("PV1-8.9", "ADT&GLN");
            //responseMessage.SetValueAtPatrecievedh("PID-3.1", "sdf");
            ////responseMessage.SetValueAtPath("PID-3[2].1", "");
            ////responseMessage.SetValueAtPath("PID-3[4].5", "asdfs");
            ////responseMessage.SetValueAtPath("PID-3[2].1", "ob");
            ////responseMessage.SetValueAtPath("PID-3[8].1", "odb");

            //responseMessage.SetValueAtPath("PID-3[2].2", "PID-3[2].2");
            //responseMessage.SetValueAtPath("PID-3[2].3", "PID-3[2].3");
            //responseMessage.SetValueAtPath("PID-3[2].4", "PID-3[2].4");
            //responseMessage.SetValueAtPath("PID-3[2].5", "PID-3[2].5");
            //responseMessage.SetValueAtPath("PID-3[2].6", "PID-3[2].6");

            //responseMessage.SetValueAtPath("PID-4", "PID-4");

            //responseMessage.SetValueAtPath("PID-5.1", "PID-5.1");
            //responseMessage.SetValueAtPath("PID-5.2", "PID-5.2");
            ////responseMessage.SetValueAtPath("PID-3[8].3", "PID-3[8].3");
            ////responseMessage.SetValueAtPath("PID-3[8].5", "PID-3[8].5");
            ////responseMessage.SetValueAtPath("PID-3[9].5", "PID-3[9].5");
            ////responseMessage.SetValueAtPath("PID-3[9].1", "PID-3[9].1");
            ////responseMessage.SetValueAtPath("PID-3[6].1", "PID-3[6].1");
            ////responseMessage.SetValueAtPath("PID-3[7]", "PID-3[7]");
            ////responseMessage.SetValueAtPath("PID-5[7].2.8", "PID-5[7].2.8");
            ////responseMessage.SetValueAtPath("PID-5[9].2.8", "PID-5[9].2.8");
            ////responseMessage.SetValueAtPath("PID-5[3].2.3", "PID-5[3].2.3");
        }


    }
}
