/* Additive narrative storage. Run only through the controlled SQL baseline process. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.tblNORM_NarrativeBlock', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNORM_NarrativeBlock (
        NarrativeBlockId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblNORM_NarrativeBlock PRIMARY KEY,
        ConfigurationReleaseId INT NOT NULL,
        DestinationKind VARCHAR(12) NOT NULL,
        DestinationKey VARCHAR(120) NOT NULL,
        ContentType VARCHAR(20) NOT NULL,
        Placement VARCHAR(6) NOT NULL,
        SequenceNo SMALLINT NOT NULL,
        ContentHtml NVARCHAR(MAX) NOT NULL,
        StatusCode VARCHAR(8) NOT NULL,
        SourceBlockId BIGINT NULL,
        UpdatedBy NVARCHAR(256) NOT NULL,
        UpdatedUtc DATETIME2(3) NOT NULL CONSTRAINT DF_tblNORM_NarrativeBlock_Updated DEFAULT (SYSUTCDATETIME()),
        Revision ROWVERSION NOT NULL,
        CONSTRAINT FK_tblNORM_NarrativeBlock_Release FOREIGN KEY (ConfigurationReleaseId)
            REFERENCES dbo.tblNORM_ConfigurationRelease (ConfigurationReleaseId),
        CONSTRAINT FK_tblNORM_NarrativeBlock_Source FOREIGN KEY (SourceBlockId)
            REFERENCES dbo.tblNORM_NarrativeBlock (NarrativeBlockId),
        CONSTRAINT UQ_tblNORM_NarrativeBlock_Position UNIQUE
            (ConfigurationReleaseId, DestinationKind, DestinationKey, Placement, SequenceNo),
        CONSTRAINT CK_tblNORM_NarrativeBlock_Destination CHECK
            (DestinationKind IN ('Overview','Statement','Section','Note','Schedule') AND LEN(DestinationKey) > 0),
        CONSTRAINT CK_tblNORM_NarrativeBlock_Type CHECK
            (ContentType IN ('Disclosure','AccountingPolicy','EntityOverview','SectionIntroduction')),
        CONSTRAINT CK_tblNORM_NarrativeBlock_Placement CHECK (Placement IN ('Before','After') AND SequenceNo BETWEEN 1 AND 999),
        CONSTRAINT CK_tblNORM_NarrativeBlock_Contents CHECK (DATALENGTH(ContentHtml) BETWEEN 2 AND 40000),
        CONSTRAINT CK_tblNORM_NarrativeBlock_Status CHECK (StatusCode IN ('Draft','Prepared'))
    );
    CREATE UNIQUE INDEX UX_tblNORM_NarrativeBlock_Rollover ON dbo.tblNORM_NarrativeBlock (ConfigurationReleaseId, SourceBlockId)
        WHERE SourceBlockId IS NOT NULL;
END;

COMMIT TRANSACTION;
