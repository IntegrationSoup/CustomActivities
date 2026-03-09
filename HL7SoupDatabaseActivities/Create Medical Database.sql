/****** Object:  Table [dbo].[Appointment]    Script Date: 15/04/2017 10:20:52 AM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[Appointment](
	[AppointmentID] [nvarchar](50) NOT NULL,
	[StartDate] [datetime] NULL,
	[EndDate] [datetime] NULL,
	[PatientID] [int] NULL,
 CONSTRAINT [PK_Appointment] PRIMARY KEY CLUSTERED 
(
	[AppointmentID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
)

GO

CREATE TABLE [dbo].[Patient](
	[PatientId] [int] IDENTITY(1,1) NOT NULL,
	[FirstName] [nvarchar](50) NULL,
	[LastName] [nvarchar](50) NULL,
	[BirthDate] [datetime] NULL,
	[ExternalPatientID] [nvarchar](50) NOT NULL,
	[Uid] [uniqueidentifier] NULL,
	[ChangedFlag] [int] NOT NULL,
 CONSTRAINT [PK_Patient] PRIMARY KEY CLUSTERED 
(
	[PatientId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
)
GO

ALTER TABLE [dbo].[Patient] ADD  CONSTRAINT [DF_Patient_uid]  DEFAULT (newid()) FOR [uid]
GO

ALTER TABLE [dbo].[Patient] ADD  CONSTRAINT [DF_Patient_ChangedFlag]  DEFAULT ((0)) FOR [ChangedFlag]

GO

DROP TRIGGER IF EXISTS PatientChanged

GO

CREATE TRIGGER dbo.PatientChanged ON dbo.Patient AFTER INSERT, UPDATE 

AS BEGIN 

    --- FILL THE BEGIN/END SECTION FOR YOUR NEEDS.

    SET NOCOUNT ON;

    IF EXISTS(SELECT * FROM INSERTED)  AND EXISTS(SELECT * FROM DELETED) 
        BEGIN 
			--Patients Updated
			UPDATE Patient Set ChangedFlag = 2 where ChangedFlag = 0 and PatientID in (Select Inserted.PatientID from Inserted inner join Deleted on Inserted.PatientId = Deleted.PatientId and Inserted.ChangedFlag = Deleted.Changedflag)
		END 
    ELSE IF EXISTS(SELECT * FROM INSERTED)  AND NOT EXISTS(SELECT * FROM DELETED) 
        BEGIN 
			--Patients inserted
			UPDATE Patient Set ChangedFlag = 1 where ChangedFlag = 0 and PatientID in (Select PatientID from Inserted)
		END 

END