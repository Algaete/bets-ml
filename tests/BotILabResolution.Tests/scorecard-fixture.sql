ALTER TABLE [LabITest].BotI2026ShadowEvaluations ADD
    ConfigurationVersion NVARCHAR(80) NOT NULL DEFAULT N'v1',
    Source NVARCHAR(50) NOT NULL DEFAULT N'Pinnacle',
    SignalScore DECIMAL(12,6) NOT NULL DEFAULT 0.025,
    SelectedProbabilityMovement DECIMAL(12,6) NOT NULL DEFAULT 0.03,
    SelectedLineMovement DECIMAL(12,6) NOT NULL DEFAULT 0.5,
    OddsAgeMinutes DECIMAL(12,6) NOT NULL DEFAULT 10,
    ObservationHours DECIMAL(12,6) NOT NULL DEFAULT 2,
    PeerSnapshotId BIGINT NULL;

GO

INSERT [LabITest].BotI2026ShadowEvaluations
    (ShadowEvaluationId,FixtureIdentity,ApiFootballFixtureId,FixtureDateUtc,PredictionTimestampUtc,
     HomeTeam,ConfigurationVersion,Source,Selection,CurrentLine,SelectedOdds)
VALUES
    -- Earlier approval remains the economic selection in 30/90d; its repeated
    -- observation in the 7d window must not become an additional independent bet.
    (101,2,NULL,'2026-09-20T15:00:00','2026-09-10T14:00:00',N'Lab Spurs',N'v1',N'Betano',N'Under',2.5,2.1),
    -- A second configuration is independently ranked without duplicating its observations.
    (102,2,NULL,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Lab Spurs',N'v2',N'Pinnacle',N'Over',2.5,1.8),
    -- The exact 90-day boundary is excluded before ranking; the recent row wins selection.
    (103,88,101,'2026-09-20T15:00:00','2026-06-24T03:00:00',N'Lab Spurs',N'v1',N'Betano',N'Under',2.5,2.1),
    (104,88,101,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Lab Spurs',N'v1',N'Pinnacle',N'Over',2.5,1.9),
    -- Future predictions and nonselected decisions still obey the read cutoff.
    (105,89,101,'2026-09-25T15:00:00','2026-09-23T14:00:00',N'Lab Spurs',N'v1',N'Pinnacle',N'Over',2.5,1.9),
    (106,90,101,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Lab Spurs',N'v1',N'Betano',N'Over',2.5,1.9),
    -- Equal timestamps are resolved deterministically by the lowest audit id.
    (107,91,101,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Lab Spurs',N'v1',N'Pinnacle',N'Over',2.5,1.9),
    (108,91,101,'2026-09-20T15:00:00','2026-09-20T14:00:00',N'Lab Spurs',N'v1',N'Betano',N'Over',3.5,2.1);
UPDATE [LabITest].BotI2026ShadowEvaluations SET Decision=N'Abstain', SignalScore=0.01 WHERE ShadowEvaluationId=106;
UPDATE [LabITest].BotI2026ShadowEvaluations SET PeerSnapshotId=1000 WHERE ShadowEvaluationId IN (2,14,102,108);
