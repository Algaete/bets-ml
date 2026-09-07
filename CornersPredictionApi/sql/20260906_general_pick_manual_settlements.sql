IF OBJECT_ID(N'dbo.GeneralBotPickManualSettlements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GeneralBotPickManualSettlements
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RecordId BIGINT NOT NULL,
        RequestId UNIQUEIDENTIFIER NOT NULL,
        ActualValue INT NOT NULL CHECK (ActualValue BETWEEN 0 AND 1000),
        OutcomeStatus NVARCHAR(20) NOT NULL,
        SettlementFactor DECIMAL(9,4) NOT NULL,
        ProfitLoss DECIMAL(12,4) NOT NULL,
        Reason NVARCHAR(1000) NOT NULL,
        SettledBy NVARCHAR(256) NOT NULL,
        SettledAtUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_GeneralBotPickManualSettlement_Request UNIQUE (RequestId)
    );
    CREATE INDEX IX_GeneralBotPickManualSettlement_Record
        ON dbo.GeneralBotPickManualSettlements (RecordId, Id DESC)
        INCLUDE (ActualValue, OutcomeStatus, SettlementFactor, ProfitLoss, Reason, SettledBy, SettledAtUtc);
END;
