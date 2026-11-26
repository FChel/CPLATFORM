/* Additive migration. Apply through the explicit schema baseline and migration process. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.tblNORM_ManualSchedule', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ManualSchedule (
        ManualScheduleId INT IDENTITY PRIMARY KEY,
        EntityCode VARCHAR(20) NOT NULL,
        ReportingStream VARCHAR(20) NOT NULL,
        ScheduleCode VARCHAR(40) NOT NULL,
        EffectiveYear INT NOT NULL,
        Definition NVARCHAR(MAX) NOT NULL CHECK (ISJSON(Definition) = 1),
        CONSTRAINT UQ_tblNORM_ManualSchedule UNIQUE (EntityCode, ReportingStream, ScheduleCode, EffectiveYear)
    );
END;

IF OBJECT_ID('dbo.tblNORM_ManualValueRevision', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ManualValueRevision (
        RevisionId BIGINT IDENTITY PRIMARY KEY,
        EntityCode VARCHAR(20) NOT NULL,
        ReportingStream VARCHAR(20) NOT NULL,
        ReportingYear INT NOT NULL,
        ScheduleCode VARCHAR(40) NOT NULL,
        InputCode VARCHAR(40) NOT NULL,
        IsComparative BIT NOT NULL,
        ManualScheduleId INT NOT NULL REFERENCES dbo.tblNORM_ManualSchedule(ManualScheduleId),
        Amount DECIMAL(19,3) NULL,
        Unit NVARCHAR(24) NOT NULL,
        InputPrecision TINYINT NOT NULL CHECK (InputPrecision BETWEEN 0 AND 3),
        SourceReference NVARCHAR(500) NULL,
        PreviousRevisionId BIGINT NULL REFERENCES dbo.tblNORM_ManualValueRevision(RevisionId),
        OriginalPriorRevisionId BIGINT NULL REFERENCES dbo.tblNORM_ManualValueRevision(RevisionId),
        ChangeReason NVARCHAR(500) NULL,
        EnteredBy NVARCHAR(256) NOT NULL,
        EnteredUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_tblNORM_ManualValueRevision_Evidence CHECK (Amount IS NULL OR SourceReference IS NOT NULL)
    );
    CREATE INDEX IX_tblNORM_ManualValueRevision_Annual ON dbo.tblNORM_ManualValueRevision
        (EntityCode, ReportingStream, ReportingYear, ScheduleCode, InputCode, IsComparative, RevisionId DESC);
END;

IF OBJECT_ID('dbo.tblNORM_ManualScheduleEvidence', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_ManualScheduleEvidence (
        EvidenceId BIGINT IDENTITY PRIMARY KEY,
        CalculationRunId INT NOT NULL REFERENCES dbo.tblNORM_CalculationRun(CalculationRunId),
        ManualScheduleId INT NOT NULL REFERENCES dbo.tblNORM_ManualSchedule(ManualScheduleId),
        EvidenceHash CHAR(64) NOT NULL,
        EvidenceJson NVARCHAR(MAX) NOT NULL CHECK (ISJSON(EvidenceJson) = 1),
        RecordedBy NVARCHAR(256) NOT NULL,
        RecordedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_tblNORM_ManualScheduleEvidence UNIQUE (CalculationRunId, ManualScheduleId, EvidenceHash)
    );
END;

/* Effective-year definitions are reusable. Add a new effective-year row for a changed layout.
   UnitFactor expresses dollars per unit. The calculation engine stores links in A$'000.
   Terms use a row's key and a sign of +1 or -1. Rows evaluate in the order listed. */
IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ManualSchedule
    WHERE EntityCode='NIHRACS' AND ReportingStream='Departmental' AND ScheduleCode='KMP' AND EffectiveYear=2025)
INSERT dbo.tblNORM_ManualSchedule (EntityCode,ReportingStream,ScheduleCode,EffectiveYear,Definition)
VALUES ('NIHRACS','Departmental','KMP',2025,N'{
    "Title":"Key management personnel remuneration","Note":"3.2","Section":"3","SectionTitle":"People and relationships",
    "Unit":"A$''000","UnitFactor":1000,"Precision":0,"CurrentColumn":"Current year","PriorColumn":"Comparative",
    "Rows":[
        {"Key":"STB","Label":"Short-term employee benefits","Kind":"Manual","Required":true},
        {"Key":"PEB","Label":"Post-employment benefits","Kind":"Manual","Required":true},
        {"Key":"OTH","Label":"Other long-term employee benefits","Kind":"Manual","Required":true},
        {"Key":"NMB","Label":"Non-monetary benefits","Kind":"Manual","Required":true},
        {"Key":"TOTAL","Label":"Total KMP remuneration","Kind":"Calculated","Total":true,
         "Terms":[{"Key":"STB","Sign":1},{"Key":"PEB","Sign":1},{"Key":"OTH","Sign":1},{"Key":"NMB","Sign":1}]}
    ]}');

IF NOT EXISTS (SELECT 1 FROM dbo.tblNORM_ManualSchedule
    WHERE EntityCode='NIHRACS' AND ReportingStream='Departmental' AND ScheduleCode='MATURITY' AND EffectiveYear=2025)
INSERT dbo.tblNORM_ManualSchedule (EntityCode,ReportingStream,ScheduleCode,EffectiveYear,Definition)
VALUES ('NIHRACS','Departmental','MATURITY',2025,N'{
    "Title":"Current / non-current classification","Note":"5.1","Section":"5","SectionTitle":"Other information",
    "Unit":"A$''000","UnitFactor":1000,"Precision":0,"CurrentColumn":"Current year","PriorColumn":"Comparative",
    "Rows":[
        {"Key":"PRV_TOTAL","Label":"Employee provisions \u2014 linked total","Kind":"Linked",
         "Statement":"SOFP","Line":"Employee provisions","Total":true},
        {"Key":"PRV_NON","Label":"Employee provisions \u2014 non-current","Kind":"Manual","Required":true},
        {"Key":"PRV_CUR","Label":"Employee provisions \u2014 current","Kind":"Calculated",
         "Terms":[{"Key":"PRV_TOTAL","Sign":1},{"Key":"PRV_NON","Sign":-1}]},
        {"Key":"LEA_TOTAL","Label":"Lease liabilities \u2014 linked total","Kind":"Linked",
         "Statement":"SOFP","Line":"Leases","Total":true},
        {"Key":"LEA_NON","Label":"Lease liabilities \u2014 non-current","Kind":"Manual","Required":true},
        {"Key":"LEA_CUR","Label":"Lease liabilities \u2014 current","Kind":"Calculated",
         "Terms":[{"Key":"LEA_TOTAL","Sign":1},{"Key":"LEA_NON","Sign":-1}]}
    ],
    "Splits":[{"Linked":"PRV_TOTAL","Entered":"PRV_NON","Residual":"PRV_CUR"},
              {"Linked":"LEA_TOTAL","Entered":"LEA_NON","Residual":"LEA_CUR"}]
}');

COMMIT TRANSACTION;
