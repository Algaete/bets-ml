IF OBJECT_ID('dbo.BettingRecords', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BettingRecords
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BettingRecords PRIMARY KEY,
        UserId NVARCHAR(450) NOT NULL CONSTRAINT DF_BettingRecords_UserId DEFAULT 'local-user',
        CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_BettingRecords_CurrencyCode DEFAULT 'CLP',
        League NVARCHAR(100) NOT NULL,
        Season NVARCHAR(20) NOT NULL,
        MatchDate DATE NOT NULL,
        HomeTeam NVARCHAR(150) NOT NULL,
        AwayTeam NVARCHAR(150) NOT NULL,
        Bookmaker NVARCHAR(100) NULL,
        MarketType NVARCHAR(50) NOT NULL,
        BetSelection NVARCHAR(50) NOT NULL,
        Line DECIMAL(10,2) NOT NULL,
        Odds DECIMAL(10,2) NOT NULL,
        Stake DECIMAL(18,2) NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        ActualHomeCorners INT NULL,
        ActualAwayCorners INT NULL,
        ActualTotalCorners INT NULL,
        ActualHomeShots INT NULL,
        ActualAwayShots INT NULL,
        ActualTotalShots INT NULL,
        ActualHomeShotsOnGoal INT NULL,
        ActualAwayShotsOnGoal INT NULL,
        ActualTotalShotsOnGoal INT NULL,
        CashoutAmount DECIMAL(18,2) NULL,
        PotentialReturn DECIMAL(18,2) NOT NULL,
        NetReturn DECIMAL(18,2) NOT NULL,
        ProfitLoss DECIMAL(18,2) NOT NULL,
        RoiPercent DECIMAL(10,2) NOT NULL,
        BankrollBefore DECIMAL(18,2) NULL,
        BankrollAfter DECIMAL(18,2) NULL,
        ClosingOdds DECIMAL(10,2) NULL,
        ConfidenceLevel NVARCHAR(20) NULL,
        PredictionModel NVARCHAR(50) NOT NULL CONSTRAINT DF_BettingRecords_PredictionModel DEFAULT 'Manual',
        Notes NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_BettingRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(0) NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_BettingRecords_IsDeleted DEFAULT 0,
        CONSTRAINT CK_BettingRecords_CurrencyCode CHECK (CurrencyCode IN ('CLP', 'USD', 'AUD')),
        CONSTRAINT CK_BettingRecords_Status CHECK (Status IN ('Pending', 'Won', 'Lost', 'Void', 'Cashout')),
        CONSTRAINT CK_BettingRecords_MarketType CHECK (MarketType IN ('TotalCorners', 'HomeCorners', 'AwayCorners', 'FirstHalfCorners', 'TotalShots', 'TotalShotsOnGoal', 'Other')),
        CONSTRAINT CK_BettingRecords_BetSelection CHECK (BetSelection IN ('Over', 'Under', 'Home', 'Away', 'Other')),
        CONSTRAINT CK_BettingRecords_ConfidenceLevel CHECK (ConfidenceLevel IS NULL OR ConfidenceLevel IN ('Low', 'Medium', 'High')),
        CONSTRAINT CK_BettingRecords_PredictionModel CHECK (PredictionModel IN ('Manual', 'TotalCornersModel', 'OverUnderLineModel', 'ShotsOnGoalModel')),
        CONSTRAINT CK_BettingRecords_Line CHECK (Line >= 0),
        CONSTRAINT CK_BettingRecords_Odds CHECK (Odds > 1),
        CONSTRAINT CK_BettingRecords_Stake CHECK (Stake > 0),
        CONSTRAINT CK_BettingRecords_CashoutAmount CHECK (CashoutAmount IS NULL OR CashoutAmount >= 0),
        CONSTRAINT CK_BettingRecords_Corners CHECK
        (
            (ActualHomeCorners IS NULL OR ActualHomeCorners >= 0) AND
            (ActualAwayCorners IS NULL OR ActualAwayCorners >= 0) AND
            (ActualTotalCorners IS NULL OR ActualTotalCorners >= 0) AND
            (ActualHomeShots IS NULL OR ActualHomeShots >= 0) AND
            (ActualAwayShots IS NULL OR ActualAwayShots >= 0) AND
            (ActualTotalShots IS NULL OR ActualTotalShots >= 0) AND
            (ActualHomeShotsOnGoal IS NULL OR ActualHomeShotsOnGoal >= 0) AND
            (ActualAwayShotsOnGoal IS NULL OR ActualAwayShotsOnGoal >= 0) AND
            (ActualTotalShotsOnGoal IS NULL OR ActualTotalShotsOnGoal >= 0)
        )
    );

    CREATE INDEX IX_BettingRecords_Date ON dbo.BettingRecords(MatchDate) INCLUDE (Status, Stake, ProfitLoss);
    CREATE INDEX IX_BettingRecords_Filters ON dbo.BettingRecords(UserId, League, Season, Status, MarketType, Bookmaker, IsDeleted);
END
GO

IF COL_LENGTH('dbo.BettingRecords', 'ActualHomeShots') IS NULL
BEGIN
    ALTER TABLE dbo.BettingRecords ADD
        ActualHomeShots INT NULL,
        ActualAwayShots INT NULL,
        ActualTotalShots INT NULL,
        ActualHomeShotsOnGoal INT NULL,
        ActualAwayShotsOnGoal INT NULL,
        ActualTotalShotsOnGoal INT NULL;
END
GO

IF COL_LENGTH('dbo.BettingRecords', 'UserId') IS NULL
BEGIN
    ALTER TABLE dbo.BettingRecords
        ADD UserId NVARCHAR(450) NOT NULL
            CONSTRAINT DF_BettingRecords_UserId DEFAULT 'local-user' WITH VALUES;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_BettingRecords_UserCurrency'
      AND object_id = OBJECT_ID('dbo.BettingRecords')
)
BEGIN
    CREATE INDEX IX_BettingRecords_UserCurrency
        ON dbo.BettingRecords(UserId, CurrencyCode, IsDeleted, MatchDate DESC, Id DESC);
END
GO

IF COL_LENGTH('dbo.BettingRecords', 'CurrencyCode') IS NULL
BEGIN
    ALTER TABLE dbo.BettingRecords
        ADD CurrencyCode CHAR(3) NOT NULL
            CONSTRAINT DF_BettingRecords_CurrencyCode DEFAULT 'CLP' WITH VALUES;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_BettingRecords_CurrencyCode'
      AND parent_object_id = OBJECT_ID('dbo.BettingRecords')
)
BEGIN
    ALTER TABLE dbo.BettingRecords
        ADD CONSTRAINT CK_BettingRecords_CurrencyCode CHECK (CurrencyCode IN ('CLP', 'USD', 'AUD'));
END
GO

IF COL_LENGTH('dbo.BettingRecords', 'PredictionModel') IS NULL
BEGIN
    ALTER TABLE dbo.BettingRecords
        ADD PredictionModel NVARCHAR(50) NOT NULL
            CONSTRAINT DF_BettingRecords_PredictionModel DEFAULT 'Manual' WITH VALUES;
END
GO

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_BettingRecords_MarketType'
      AND parent_object_id = OBJECT_ID('dbo.BettingRecords')
)
BEGIN
    ALTER TABLE dbo.BettingRecords DROP CONSTRAINT CK_BettingRecords_MarketType;
END
GO

ALTER TABLE dbo.BettingRecords
    ADD CONSTRAINT CK_BettingRecords_MarketType CHECK (MarketType IN ('TotalCorners', 'HomeCorners', 'AwayCorners', 'FirstHalfCorners', 'TotalShots', 'TotalShotsOnGoal', 'Other'));
GO

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_BettingRecords_PredictionModel'
      AND parent_object_id = OBJECT_ID('dbo.BettingRecords')
)
BEGIN
    ALTER TABLE dbo.BettingRecords DROP CONSTRAINT CK_BettingRecords_PredictionModel;
END
GO

ALTER TABLE dbo.BettingRecords
    ADD CONSTRAINT CK_BettingRecords_PredictionModel CHECK (PredictionModel IN ('Manual', 'TotalCornersModel', 'OverUnderLineModel', 'ShotsOnGoalModel'));
GO

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_BettingRecords_Corners'
      AND parent_object_id = OBJECT_ID('dbo.BettingRecords')
)
BEGIN
    ALTER TABLE dbo.BettingRecords DROP CONSTRAINT CK_BettingRecords_Corners;
END
GO

ALTER TABLE dbo.BettingRecords
    ADD CONSTRAINT CK_BettingRecords_Corners CHECK
    (
        (ActualHomeCorners IS NULL OR ActualHomeCorners >= 0) AND
        (ActualAwayCorners IS NULL OR ActualAwayCorners >= 0) AND
        (ActualTotalCorners IS NULL OR ActualTotalCorners >= 0) AND
        (ActualHomeShots IS NULL OR ActualHomeShots >= 0) AND
        (ActualAwayShots IS NULL OR ActualAwayShots >= 0) AND
        (ActualTotalShots IS NULL OR ActualTotalShots >= 0) AND
        (ActualHomeShotsOnGoal IS NULL OR ActualHomeShotsOnGoal >= 0) AND
        (ActualAwayShotsOnGoal IS NULL OR ActualAwayShotsOnGoal >= 0) AND
        (ActualTotalShotsOnGoal IS NULL OR ActualTotalShotsOnGoal >= 0)
    );
GO

CREATE OR ALTER PROCEDURE dbo.sp_InsertBettingRecord
    @UserId NVARCHAR(450) = 'local-user',
    @CurrencyCode CHAR(3) = 'CLP',
    @League NVARCHAR(100),
    @Season NVARCHAR(20),
    @MatchDate DATE,
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @Bookmaker NVARCHAR(100) = NULL,
    @MarketType NVARCHAR(50),
    @BetSelection NVARCHAR(50),
    @Line DECIMAL(10,2),
    @Odds DECIMAL(10,2),
    @Stake DECIMAL(18,2),
    @Status NVARCHAR(20),
    @ActualHomeCorners INT = NULL,
    @ActualAwayCorners INT = NULL,
    @ActualTotalCorners INT = NULL,
    @ActualHomeShots INT = NULL,
    @ActualAwayShots INT = NULL,
    @ActualTotalShots INT = NULL,
    @ActualHomeShotsOnGoal INT = NULL,
    @ActualAwayShotsOnGoal INT = NULL,
    @ActualTotalShotsOnGoal INT = NULL,
    @CashoutAmount DECIMAL(18,2) = NULL,
    @PotentialReturn DECIMAL(18,2),
    @NetReturn DECIMAL(18,2),
    @ProfitLoss DECIMAL(18,2),
    @RoiPercent DECIMAL(10,2),
    @BankrollBefore DECIMAL(18,2) = NULL,
    @BankrollAfter DECIMAL(18,2) = NULL,
    @ClosingOdds DECIMAL(10,2) = NULL,
    @ConfidenceLevel NVARCHAR(20) = NULL,
    @PredictionModel NVARCHAR(50) = 'Manual',
    @Notes NVARCHAR(MAX) = NULL,
    @InsertedId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserFilter NVARCHAR(450) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user');

    INSERT dbo.BettingRecords
    (
        UserId, League, Season, MatchDate, HomeTeam, AwayTeam, Bookmaker, MarketType, BetSelection,
        Line, Odds, Stake, Status, ActualHomeCorners, ActualAwayCorners, ActualTotalCorners,
        ActualHomeShots, ActualAwayShots, ActualTotalShots, ActualHomeShotsOnGoal, ActualAwayShotsOnGoal,
        ActualTotalShotsOnGoal, CashoutAmount, PotentialReturn, NetReturn, ProfitLoss, RoiPercent, BankrollBefore,
        BankrollAfter, ClosingOdds, ConfidenceLevel, PredictionModel, Notes, CreatedAt, IsDeleted, CurrencyCode
    )
    VALUES
    (
        @UserFilter, LTRIM(RTRIM(@League)), LTRIM(RTRIM(@Season)), @MatchDate, LTRIM(RTRIM(@HomeTeam)),
        LTRIM(RTRIM(@AwayTeam)), NULLIF(LTRIM(RTRIM(@Bookmaker)), ''), @MarketType, @BetSelection,
        @Line, @Odds, @Stake, @Status, @ActualHomeCorners, @ActualAwayCorners, @ActualTotalCorners,
        @ActualHomeShots, @ActualAwayShots, @ActualTotalShots, @ActualHomeShotsOnGoal, @ActualAwayShotsOnGoal,
        @ActualTotalShotsOnGoal, @CashoutAmount, @PotentialReturn, @NetReturn, @ProfitLoss, @RoiPercent, @BankrollBefore,
        @BankrollAfter, @ClosingOdds, @ConfidenceLevel, COALESCE(NULLIF(LTRIM(RTRIM(@PredictionModel)), ''), 'Manual'),
        NULLIF(LTRIM(RTRIM(@Notes)), ''), SYSUTCDATETIME(), 0,
        UPPER(@CurrencyCode)
    );

    SET @InsertedId = CONVERT(BIGINT, SCOPE_IDENTITY());
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateBettingRecord
    @Id BIGINT,
    @UserId NVARCHAR(450) = 'local-user',
    @CurrencyCode CHAR(3) = 'CLP',
    @League NVARCHAR(100),
    @Season NVARCHAR(20),
    @MatchDate DATE,
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @Bookmaker NVARCHAR(100) = NULL,
    @MarketType NVARCHAR(50),
    @BetSelection NVARCHAR(50),
    @Line DECIMAL(10,2),
    @Odds DECIMAL(10,2),
    @Stake DECIMAL(18,2),
    @Status NVARCHAR(20),
    @ActualHomeCorners INT = NULL,
    @ActualAwayCorners INT = NULL,
    @ActualTotalCorners INT = NULL,
    @ActualHomeShots INT = NULL,
    @ActualAwayShots INT = NULL,
    @ActualTotalShots INT = NULL,
    @ActualHomeShotsOnGoal INT = NULL,
    @ActualAwayShotsOnGoal INT = NULL,
    @ActualTotalShotsOnGoal INT = NULL,
    @CashoutAmount DECIMAL(18,2) = NULL,
    @PotentialReturn DECIMAL(18,2),
    @NetReturn DECIMAL(18,2),
    @ProfitLoss DECIMAL(18,2),
    @RoiPercent DECIMAL(10,2),
    @BankrollBefore DECIMAL(18,2) = NULL,
    @BankrollAfter DECIMAL(18,2) = NULL,
    @ClosingOdds DECIMAL(10,2) = NULL,
    @ConfidenceLevel NVARCHAR(20) = NULL,
    @PredictionModel NVARCHAR(50) = 'Manual',
    @Notes NVARCHAR(MAX) = NULL,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserFilter NVARCHAR(450) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user');

    UPDATE dbo.BettingRecords
    SET
        League = LTRIM(RTRIM(@League)),
        CurrencyCode = UPPER(@CurrencyCode),
        Season = LTRIM(RTRIM(@Season)),
        MatchDate = @MatchDate,
        HomeTeam = LTRIM(RTRIM(@HomeTeam)),
        AwayTeam = LTRIM(RTRIM(@AwayTeam)),
        Bookmaker = NULLIF(LTRIM(RTRIM(@Bookmaker)), ''),
        MarketType = @MarketType,
        BetSelection = @BetSelection,
        Line = @Line,
        Odds = @Odds,
        Stake = @Stake,
        Status = @Status,
        ActualHomeCorners = @ActualHomeCorners,
        ActualAwayCorners = @ActualAwayCorners,
        ActualTotalCorners = @ActualTotalCorners,
        ActualHomeShots = @ActualHomeShots,
        ActualAwayShots = @ActualAwayShots,
        ActualTotalShots = @ActualTotalShots,
        ActualHomeShotsOnGoal = @ActualHomeShotsOnGoal,
        ActualAwayShotsOnGoal = @ActualAwayShotsOnGoal,
        ActualTotalShotsOnGoal = @ActualTotalShotsOnGoal,
        CashoutAmount = @CashoutAmount,
        PotentialReturn = @PotentialReturn,
        NetReturn = @NetReturn,
        ProfitLoss = @ProfitLoss,
        RoiPercent = @RoiPercent,
        BankrollBefore = @BankrollBefore,
        BankrollAfter = @BankrollAfter,
        ClosingOdds = @ClosingOdds,
        ConfidenceLevel = @ConfidenceLevel,
        PredictionModel = COALESCE(NULLIF(LTRIM(RTRIM(@PredictionModel)), ''), 'Manual'),
        Notes = NULLIF(LTRIM(RTRIM(@Notes)), ''),
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id
      AND UserId = @UserFilter
      AND IsDeleted = 0;

    SET @RowsAffected = @@ROWCOUNT;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DeleteBettingRecord
    @Id BIGINT,
    @UserId NVARCHAR(450) = 'local-user',
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.BettingRecords
    SET IsDeleted = 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id
      AND UserId = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user')
      AND IsDeleted = 0;

    SET @RowsAffected = @@ROWCOUNT;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBettingRecordById
    @Id BIGINT,
    @UserId NVARCHAR(450) = 'local-user'
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM dbo.BettingRecords
    WHERE Id = @Id
      AND UserId = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user')
      AND IsDeleted = 0;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBettingRecords
    @UserId NVARCHAR(450) = 'local-user',
    @CurrencyCode CHAR(3) = NULL,
    @League NVARCHAR(100) = NULL,
    @Season NVARCHAR(20) = NULL,
    @HomeTeam NVARCHAR(150) = NULL,
    @AwayTeam NVARCHAR(150) = NULL,
    @Status NVARCHAR(20) = NULL,
    @MarketType NVARCHAR(50) = NULL,
    @Bookmaker NVARCHAR(100) = NULL,
    @DateFrom DATE = NULL,
    @DateTo DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserFilter NVARCHAR(450) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user');
    DECLARE @CurrencyFilter CHAR(3) = NULLIF(UPPER(LTRIM(RTRIM(@CurrencyCode))), '');

    SELECT *
    FROM dbo.BettingRecords
    WHERE IsDeleted = 0
      AND UserId = @UserFilter
      AND (@CurrencyFilter IS NULL OR CurrencyCode = @CurrencyFilter)
      AND (@League IS NULL OR League = @League)
      AND (@Season IS NULL OR Season = @Season)
      AND (@HomeTeam IS NULL OR HomeTeam LIKE '%' + @HomeTeam + '%')
      AND (@AwayTeam IS NULL OR AwayTeam LIKE '%' + @AwayTeam + '%')
      AND (@Status IS NULL OR Status = @Status)
      AND (@MarketType IS NULL OR MarketType = @MarketType)
      AND (@Bookmaker IS NULL OR Bookmaker = @Bookmaker)
      AND (@DateFrom IS NULL OR MatchDate >= @DateFrom)
      AND (@DateTo IS NULL OR MatchDate <= @DateTo)
    ORDER BY MatchDate DESC, Id DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBettingSummary
    @UserId NVARCHAR(450) = 'local-user',
    @CurrencyCode CHAR(3) = NULL,
    @League NVARCHAR(100) = NULL,
    @Season NVARCHAR(20) = NULL,
    @HomeTeam NVARCHAR(150) = NULL,
    @AwayTeam NVARCHAR(150) = NULL,
    @Status NVARCHAR(20) = NULL,
    @MarketType NVARCHAR(50) = NULL,
    @Bookmaker NVARCHAR(100) = NULL,
    @DateFrom DATE = NULL,
    @DateTo DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserFilter NVARCHAR(450) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user');
    DECLARE @CurrencyFilter CHAR(3) = NULLIF(UPPER(LTRIM(RTRIM(@CurrencyCode))), '');

    WITH Filtered AS
    (
        SELECT *
        FROM dbo.BettingRecords
        WHERE IsDeleted = 0
          AND UserId = @UserFilter
          AND (@CurrencyFilter IS NULL OR CurrencyCode = @CurrencyFilter)
          AND (@League IS NULL OR League = @League)
          AND (@Season IS NULL OR Season = @Season)
          AND (@HomeTeam IS NULL OR HomeTeam LIKE '%' + @HomeTeam + '%')
          AND (@AwayTeam IS NULL OR AwayTeam LIKE '%' + @AwayTeam + '%')
          AND (@Status IS NULL OR Status = @Status)
          AND (@MarketType IS NULL OR MarketType = @MarketType)
          AND (@Bookmaker IS NULL OR Bookmaker = @Bookmaker)
          AND (@DateFrom IS NULL OR MatchDate >= @DateFrom)
          AND (@DateTo IS NULL OR MatchDate <= @DateTo)
    )
    SELECT
        TotalBets = COUNT(1),
        PendingBets = COALESCE(SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END), 0),
        WonBets = COALESCE(SUM(CASE WHEN Status = 'Won' THEN 1 ELSE 0 END), 0),
        LostBets = COALESCE(SUM(CASE WHEN Status = 'Lost' THEN 1 ELSE 0 END), 0),
        VoidBets = COALESCE(SUM(CASE WHEN Status = 'Void' THEN 1 ELSE 0 END), 0),
        CashoutBets = COALESCE(SUM(CASE WHEN Status = 'Cashout' THEN 1 ELSE 0 END), 0),
        TotalStake = COALESCE(SUM(Stake), 0),
        TotalPotentialReturn = COALESCE(SUM(PotentialReturn), 0),
        TotalNetReturn = COALESCE(SUM(NetReturn), 0),
        TotalProfitLoss = COALESCE(SUM(ProfitLoss), 0),
        RoiPercent = CASE WHEN COALESCE(SUM(Stake), 0) = 0 THEN 0 ELSE COALESCE(SUM(ProfitLoss), 0) / SUM(Stake) * 100 END,
        WinRatePercent = CASE
            WHEN SUM(CASE WHEN Status IN ('Won', 'Lost') THEN 1 ELSE 0 END) = 0 THEN 0
            ELSE CAST(SUM(CASE WHEN Status = 'Won' THEN 1 ELSE 0 END) AS DECIMAL(18,2)) /
                 SUM(CASE WHEN Status IN ('Won', 'Lost') THEN 1 ELSE 0 END) * 100
        END,
        AverageOdds = COALESCE(AVG(Odds), 0),
        AverageStake = COALESCE(AVG(Stake), 0),
        BestProfit = COALESCE(MAX(ProfitLoss), 0),
        WorstLoss = COALESCE(MIN(ProfitLoss), 0)
    FROM Filtered;
END
GO

IF OBJECT_ID('dbo.BettingBankrollTransactions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BettingBankrollTransactions
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BettingBankrollTransactions PRIMARY KEY,
        UserId NVARCHAR(450) NOT NULL CONSTRAINT DF_BettingBankrollTransactions_UserId DEFAULT 'local-user',
        CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_BettingBankrollTransactions_CurrencyCode DEFAULT 'CLP',
        TransactionDate DATE NOT NULL,
        [Type] NVARCHAR(30) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        BalanceAfter DECIMAL(18,2) NOT NULL,
        BettingRecordId BIGINT NULL,
        Notes NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_BettingBankrollTransactions_CreatedAt DEFAULT SYSUTCDATETIME(),
        IsDeleted BIT NOT NULL CONSTRAINT DF_BettingBankrollTransactions_IsDeleted DEFAULT 0,
        CONSTRAINT CK_BettingBankrollTransactions_CurrencyCode CHECK (CurrencyCode IN ('CLP', 'USD', 'AUD')),
        CONSTRAINT CK_BettingBankrollTransactions_Type CHECK ([Type] IN ('Deposit', 'Withdrawal', 'BetSettlement', 'ManualAdjustment')),
        CONSTRAINT CK_BettingBankrollTransactions_Amount CHECK (Amount <> 0),
        CONSTRAINT FK_BettingBankrollTransactions_BettingRecords
            FOREIGN KEY (BettingRecordId) REFERENCES dbo.BettingRecords(Id)
    );

    CREATE INDEX IX_BettingBankrollTransactions_Date ON dbo.BettingBankrollTransactions(UserId, TransactionDate DESC, Id DESC);
END
GO

IF COL_LENGTH('dbo.BettingBankrollTransactions', 'UserId') IS NULL
BEGIN
    ALTER TABLE dbo.BettingBankrollTransactions
        ADD UserId NVARCHAR(450) NOT NULL
            CONSTRAINT DF_BettingBankrollTransactions_UserId DEFAULT 'local-user' WITH VALUES;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_BettingBankrollTransactions_UserCurrency'
      AND object_id = OBJECT_ID('dbo.BettingBankrollTransactions')
)
BEGIN
    CREATE INDEX IX_BettingBankrollTransactions_UserCurrency
        ON dbo.BettingBankrollTransactions(UserId, CurrencyCode, IsDeleted, Id DESC);
END
GO

IF COL_LENGTH('dbo.BettingBankrollTransactions', 'CurrencyCode') IS NULL
BEGIN
    ALTER TABLE dbo.BettingBankrollTransactions
        ADD CurrencyCode CHAR(3) NOT NULL
            CONSTRAINT DF_BettingBankrollTransactions_CurrencyCode DEFAULT 'CLP' WITH VALUES;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CK_BettingBankrollTransactions_CurrencyCode'
      AND parent_object_id = OBJECT_ID('dbo.BettingBankrollTransactions')
)
BEGIN
    ALTER TABLE dbo.BettingBankrollTransactions
        ADD CONSTRAINT CK_BettingBankrollTransactions_CurrencyCode CHECK (CurrencyCode IN ('CLP', 'USD', 'AUD'));
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_InsertBankrollTransaction
    @UserId NVARCHAR(450) = 'local-user',
    @CurrencyCode CHAR(3) = 'CLP',
    @TransactionDate DATE,
    @Type NVARCHAR(30),
    @Amount DECIMAL(18,2),
    @BettingRecordId BIGINT = NULL,
    @Notes NVARCHAR(MAX) = NULL,
    @InsertedId BIGINT OUTPUT,
    @BalanceAfter DECIMAL(18,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserFilter NVARCHAR(450) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user');

    DECLARE @CurrentBalance DECIMAL(18,2) =
    (
        SELECT TOP (1) BalanceAfter
        FROM dbo.BettingBankrollTransactions
        WHERE IsDeleted = 0
          AND UserId = @UserFilter
          AND CurrencyCode = UPPER(@CurrencyCode)
        ORDER BY Id DESC
    );

    SET @CurrentBalance = COALESCE(@CurrentBalance, 0);
    SET @BalanceAfter = @CurrentBalance + @Amount;

    INSERT dbo.BettingBankrollTransactions
    (
        UserId,
        TransactionDate,
        CurrencyCode,
        [Type],
        Amount,
        BalanceAfter,
        BettingRecordId,
        Notes,
        CreatedAt,
        IsDeleted
    )
    VALUES
    (
        @UserFilter,
        @TransactionDate,
        UPPER(@CurrencyCode),
        @Type,
        @Amount,
        @BalanceAfter,
        @BettingRecordId,
        NULLIF(LTRIM(RTRIM(@Notes)), ''),
        SYSUTCDATETIME(),
        0
    );

    SET @InsertedId = CONVERT(BIGINT, SCOPE_IDENTITY());
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_ReconcileBetSettlementTransaction
    @UserId NVARCHAR(450) = 'local-user',
    @CurrencyCode CHAR(3) = 'CLP',
    @TransactionDate DATE,
    @BettingRecordId BIGINT,
    @DesiredAmount DECIMAL(18,2),
    @Notes NVARCHAR(MAX) = NULL,
    @InsertedId BIGINT OUTPUT,
    @BalanceAfter DECIMAL(18,2) OUTPUT,
    @AdjustmentAmount DECIMAL(18,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @UserFilter NVARCHAR(450) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user');
    DECLARE @CurrencyFilter CHAR(3) = UPPER(@CurrencyCode);

    DECLARE @PreviousSettlementAmount DECIMAL(18,2) =
    (
        SELECT COALESCE(SUM(Amount), 0)
        FROM dbo.BettingBankrollTransactions
        WHERE IsDeleted = 0
          AND UserId = @UserFilter
          AND [Type] = 'BetSettlement'
          AND BettingRecordId = @BettingRecordId
    );

    SET @AdjustmentAmount = @DesiredAmount - COALESCE(@PreviousSettlementAmount, 0);

    DECLARE @CurrentBalance DECIMAL(18,2) =
    (
        SELECT TOP (1) BalanceAfter
        FROM dbo.BettingBankrollTransactions
        WHERE IsDeleted = 0
          AND UserId = @UserFilter
          AND CurrencyCode = @CurrencyFilter
        ORDER BY Id DESC
    );

    SET @CurrentBalance = COALESCE(@CurrentBalance, 0);
    SET @BalanceAfter = @CurrentBalance;
    SET @InsertedId = 0;

    IF @AdjustmentAmount = 0
    BEGIN
        RETURN;
    END

    SET @BalanceAfter = @CurrentBalance + @AdjustmentAmount;

    INSERT dbo.BettingBankrollTransactions
    (
        UserId,
        TransactionDate,
        CurrencyCode,
        [Type],
        Amount,
        BalanceAfter,
        BettingRecordId,
        Notes,
        CreatedAt,
        IsDeleted
    )
    VALUES
    (
        @UserFilter,
        @TransactionDate,
        @CurrencyFilter,
        'BetSettlement',
        @AdjustmentAmount,
        @BalanceAfter,
        @BettingRecordId,
        NULLIF(LTRIM(RTRIM(@Notes)), ''),
        SYSUTCDATETIME(),
        0
    );

    SET @InsertedId = CONVERT(BIGINT, SCOPE_IDENTITY());
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetBankrollTransactions
    @UserId NVARCHAR(450) = 'local-user',
    @CurrencyCode CHAR(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (100)
        Id,
        UserId,
        CurrencyCode,
        TransactionDate,
        [Type],
        Amount,
        BalanceAfter,
        BettingRecordId,
        Notes,
        CreatedAt,
        IsDeleted
    FROM dbo.BettingBankrollTransactions
    WHERE IsDeleted = 0
      AND UserId = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user')
      AND (@CurrencyCode IS NULL OR CurrencyCode = UPPER(@CurrencyCode))
    ORDER BY Id DESC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetCurrentBankroll
    @UserId NVARCHAR(450) = 'local-user',
    @CurrencyCode CHAR(3) = 'CLP'
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CurrentBankroll = COALESCE(
        (
            SELECT TOP (1) BalanceAfter
            FROM dbo.BettingBankrollTransactions
            WHERE IsDeleted = 0
              AND UserId = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), ''), 'local-user')
              AND CurrencyCode = UPPER(@CurrencyCode)
            ORDER BY Id DESC
        ),
        0
    );
END
GO
