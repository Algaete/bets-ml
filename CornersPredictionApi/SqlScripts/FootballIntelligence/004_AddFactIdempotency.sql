SET NOCOUNT ON;

IF COL_LENGTH(N'dbo.FootballNewsFact', N'FactHash') IS NULL
BEGIN
    ALTER TABLE dbo.FootballNewsFact ADD FactHash CHAR(64) NULL;

    UPDATE dbo.FootballNewsFact
    SET FactHash = CONVERT(CHAR(64), HASHBYTES(
        'SHA2_256',
        CONVERT(VARBINARY(MAX), CONCAT(
            FixtureId, NCHAR(31), COALESCE(CONVERT(NVARCHAR(20), TeamId), N''), NCHAR(31),
            COALESCE(CONVERT(NVARCHAR(20), PlayerId), N''), NCHAR(31), EventType, NCHAR(31),
            AvailabilityStatus, NCHAR(31), Certainty, NCHAR(31),
            COALESCE(CONVERT(NVARCHAR(50), ProbabilityAvailable), N''), NCHAR(31),
            LTRIM(RTRIM(EvidenceSnippet)), NCHAR(31), ExtractionModel, NCHAR(31), PromptVersion
        ))), 2);

    ;WITH Duplicates AS
    (
        SELECT Id,
               ROW_NUMBER() OVER (PARTITION BY NewsDocumentId, FactHash ORDER BY Id) AS DuplicateNumber
        FROM dbo.FootballNewsFact
    )
    UPDATE fact
    SET FactHash = CONVERT(CHAR(64), HASHBYTES(
        'SHA2_256',
        CONVERT(VARBINARY(MAX), CONCAT(fact.FactHash, N':legacy:', fact.Id))), 2)
    FROM dbo.FootballNewsFact fact
    INNER JOIN Duplicates duplicate ON duplicate.Id = fact.Id
    WHERE duplicate.DuplicateNumber > 1;

    ALTER TABLE dbo.FootballNewsFact ALTER COLUMN FactHash CHAR(64) NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.FootballNewsFact')
      AND name = N'UX_FootballNewsFact_DocumentHash'
)
BEGIN
    CREATE UNIQUE INDEX UX_FootballNewsFact_DocumentHash
        ON dbo.FootballNewsFact(NewsDocumentId, FactHash);
END;
