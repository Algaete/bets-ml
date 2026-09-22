CREATE TABLE [LabITest].TeamNameAlias(AliasKey NVARCHAR(300) PRIMARY KEY, CanonicalName NVARCHAR(300));
INSERT [LabITest].TeamNameAlias VALUES
    (dbo.fn_NormalizeNameKey(N'Lab Spurs'), N'Lab Tottenham Hotspur'),
    (dbo.fn_NormalizeNameKey(N'Lab Tottenham Hotspur'), N'Lab Tottenham Hotspur');

CREATE TABLE [LabITest].MatchHistory
(
    Id BIGINT PRIMARY KEY, ApiFootballFixtureId BIGINT, MatchDate DATE,
    HomeTeam NVARCHAR(300), AwayTeam NVARCHAR(300),
    StandardizedHomeTeam NVARCHAR(300), StandardizedAwayTeam NVARCHAR(300),
    FixtureStatus NVARCHAR(10), ApiFootballUpdatedAtUtc DATETIME2(3),
    ApiFootballGoalsAvailable BIT, ApiFootballCornersAvailable BIT,
    HomeGoals INT, AwayGoals INT, HomeCorners INT, AwayCorners INT
);
INSERT [LabITest].MatchHistory VALUES
    (1,101,'2026-09-20T12:00:00',N'Lab Tottenham Hotspur',N'Lab Away',NULL,NULL,N'FT','2026-09-20T18:00:00',1,1,2,1,5,4),
    (2,102,'2026-09-20T12:00:00',N'Ambiguous Home',N'Lab Away',NULL,NULL,N'FT','2026-09-20T18:00:00',1,1,2,1,5,4),
    (3,103,'2026-09-20T12:00:00',N'Ambiguous Home',N'Lab Away',NULL,NULL,N'FT','2026-09-20T18:00:00',1,1,2,1,5,4),
    (4,104,'2026-09-20T12:00:00',N'Future Outcome',N'Lab Away',NULL,NULL,N'FT','2026-09-25T18:00:00',1,1,2,1,5,4),
    (5,105,'2026-09-20T12:00:00',N'Early Outcome',N'Lab Away',NULL,NULL,N'FT','2026-09-20T13:00:00',1,1,2,1,5,4),
    (6,106,'2026-09-20T12:00:00',N'Missing Stats',N'Lab Away',NULL,NULL,N'FT','2026-09-20T18:00:00',1,0,2,1,NULL,NULL),
    (7,107,'2026-09-20T12:00:00',N'Duplicate',N'Lab Away',NULL,NULL,N'FT','2026-09-20T18:00:00',1,1,2,1,5,4),
    (8,107,'2026-09-20T12:00:00',N'Duplicate',N'Lab Away',NULL,NULL,N'FT','2026-09-20T18:00:00',1,1,2,1,5,4),
    (9,109,'2026-09-21T23:00:00',N'Midnight',N'Lab Away',NULL,NULL,N'FT','2026-09-22T02:50:00',1,1,2,1,5,4);
ALTER TABLE [LabITest].MatchHistory ADD League NVARCHAR(300) NOT NULL DEFAULT N'Lab League', StandardizedLeague NVARCHAR(300) NULL;
ALTER TABLE [LabITest].MatchHistory ADD ApiFootballLeagueId INT NULL;
INSERT [LabITest].MatchHistory
    (Id,ApiFootballFixtureId,MatchDate,HomeTeam,AwayTeam,FixtureStatus,ApiFootballUpdatedAtUtc,
     ApiFootballGoalsAvailable,ApiFootballCornersAvailable,HomeGoals,AwayGoals,HomeCorners,AwayCorners,League,ApiFootballLeagueId)
VALUES
    (10,110,'2026-09-20',N'League ID Home',N'Lab Away',N'FT','2026-09-20T18:00:00',1,1,2,1,5,4,N'Premier League',39),
    (11,111,'2026-09-20',N'Wrong League Home',N'Lab Away',N'FT','2026-09-20T18:00:00',1,1,2,1,5,4,N'England - Premier League',140);

CREATE TABLE [LabITest].BotI2026ShadowEvaluations
(
    ShadowEvaluationId BIGINT PRIMARY KEY, FixtureIdentity BIGINT, ApiFootballFixtureId BIGINT NULL,
    FixtureDateUtc DATETIME2(3), PredictionTimestampUtc DATETIME2(3),
    HomeTeam NVARCHAR(300), AwayTeam NVARCHAR(300) DEFAULT N'Lab Away', League NVARCHAR(300) NOT NULL DEFAULT N'Lab League',
    Decision NVARCHAR(20) DEFAULT N'Approved', MarketType NVARCHAR(30) DEFAULT N'TotalGoals',
    Selection NVARCHAR(10) DEFAULT N'Over', CurrentLine DECIMAL(10,2) DEFAULT 2.5,
    SelectedOdds DECIMAL(18,6) DEFAULT 2.0
);
INSERT [LabITest].BotI2026ShadowEvaluations
    (ShadowEvaluationId,FixtureIdentity,ApiFootballFixtureId,FixtureDateUtc,PredictionTimestampUtc,HomeTeam)
VALUES
    (1,1,101,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Unrelated display name'),
    (2,2,NULL,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Lab Spurs'),
    (3,3,NULL,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Ambiguous Home'),
    (4,4,NULL,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Absent'),
    (5,5,101,'2026-09-20T15:00:00','2026-09-20T15:01:00',N'Lab Spurs'),
    (6,6,104,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Future Outcome'),
    (7,7,105,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Early Outcome'),
    (8,8,NULL,'2026-09-26T15:00:00','2026-09-20T14:00:00',N'Future Match'),
    (9,9,101,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Lab Spurs'),
    (10,10,106,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Missing Stats'),
    (11,11,107,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Duplicate'),
    (12,12,NULL,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Lab Spurs U19'),
    (13,13,NULL,'2026-09-22T02:00:00','2026-09-22T01:00:00',N'Midnight'),
    (14,2,NULL,'2026-09-20T15:00:00','2026-09-20T14:15:00',N'Lab Spurs'),
    (15,15,NULL,'2026-09-20T15:00:00','2026-09-20T14:15:00',N'Lab Spurs'),
    (16,16,NULL,'2026-09-20T15:00:00','2026-09-20T14:15:00',N'League ID Home'),
    (17,17,NULL,'2026-09-20T15:00:00','2026-09-20T14:15:00',N'Wrong League Home');
UPDATE [LabITest].BotI2026ShadowEvaluations SET MarketType=N'TotalCorners',CurrentLine=8.5 WHERE ShadowEvaluationId IN (2,10,14);
UPDATE [LabITest].BotI2026ShadowEvaluations SET Decision=N'Rejected' WHERE ShadowEvaluationId=9;
UPDATE [LabITest].BotI2026ShadowEvaluations SET League=N'Different League' WHERE ShadowEvaluationId=15;
UPDATE [LabITest].BotI2026ShadowEvaluations SET League=N'England - Premier League' WHERE ShadowEvaluationId IN (16,17);
