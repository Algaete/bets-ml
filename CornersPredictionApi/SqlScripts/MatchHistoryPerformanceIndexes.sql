IF OBJECT_ID(N'dbo.MatchHistory', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_BotPickSettlement'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_BotPickSettlement
        ON dbo.MatchHistory(MatchDate, Id)
        INCLUDE
        (
            ApiFootballFixtureId, FixtureStatus, HomeTeam, AwayTeam,
            StandardizedHomeTeam, StandardizedAwayTeam,
            HomeGoals, AwayGoals, HomeCorners, AwayCorners,
            HomeShots, AwayShots, HomeShotsOnGoal, AwayShotsOnGoal,
            ApiFootballGoalsAvailable, ApiFootballCornersAvailable,
            ApiFootballShotsAvailable, ApiFootballShotsOnGoalAvailable
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_PredictionContext_StdHome'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_PredictionContext_StdHome
        ON dbo.MatchHistory(HomeTeamGender, AwayTeamGender, StandardizedHomeTeam, MatchDate DESC, Id DESC)
        INCLUDE
        (
            League, StandardizedLeague, Season, HomeTeam, AwayTeam, StandardizedAwayTeam,
            HomeGoals, AwayGoals, HomeCorners, AwayCorners, HomeShots, AwayShots,
            HomeShotsOnGoal, AwayShotsOnGoal, HomePossession, AwayPossession,
            IsKnockout, HomeFormation, AwayFormation, CreatedAtUtc, UpdatedAtUtc, ApiFootballFixtureId
        )
        WHERE HomeCorners IS NOT NULL
          AND AwayCorners IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_PredictionContext_StdAway'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_PredictionContext_StdAway
        ON dbo.MatchHistory(HomeTeamGender, AwayTeamGender, StandardizedAwayTeam, MatchDate DESC, Id DESC)
        INCLUDE
        (
            League, StandardizedLeague, Season, HomeTeam, AwayTeam, StandardizedHomeTeam,
            HomeGoals, AwayGoals, HomeCorners, AwayCorners, HomeShots, AwayShots,
            HomeShotsOnGoal, AwayShotsOnGoal, HomePossession, AwayPossession,
            IsKnockout, HomeFormation, AwayFormation, CreatedAtUtc, UpdatedAtUtc, ApiFootballFixtureId
        )
        WHERE HomeCorners IS NOT NULL
          AND AwayCorners IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_PredictionContext_RawHome'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_PredictionContext_RawHome
        ON dbo.MatchHistory(HomeTeamGender, AwayTeamGender, HomeTeam, MatchDate DESC, Id DESC)
        INCLUDE
        (
            League, StandardizedLeague, Season, AwayTeam, StandardizedHomeTeam, StandardizedAwayTeam,
            HomeGoals, AwayGoals, HomeCorners, AwayCorners, HomeShots, AwayShots,
            HomeShotsOnGoal, AwayShotsOnGoal, HomePossession, AwayPossession,
            IsKnockout, HomeFormation, AwayFormation, CreatedAtUtc, UpdatedAtUtc, ApiFootballFixtureId
        )
        WHERE HomeCorners IS NOT NULL
          AND AwayCorners IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_PredictionContext_RawAway'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_PredictionContext_RawAway
        ON dbo.MatchHistory(HomeTeamGender, AwayTeamGender, AwayTeam, MatchDate DESC, Id DESC)
        INCLUDE
        (
            League, StandardizedLeague, Season, HomeTeam, StandardizedHomeTeam, StandardizedAwayTeam,
            HomeGoals, AwayGoals, HomeCorners, AwayCorners, HomeShots, AwayShots,
            HomeShotsOnGoal, AwayShotsOnGoal, HomePossession, AwayPossession,
            IsKnockout, HomeFormation, AwayFormation, CreatedAtUtc, UpdatedAtUtc, ApiFootballFixtureId
        )
        WHERE HomeCorners IS NOT NULL
          AND AwayCorners IS NOT NULL;
    END;

    -- Historical as-of queries filter by team before gender normalization. These
    -- team-leading indexes avoid scanning the full history for every Bot C match.
    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_AsOf_StdHome'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_AsOf_StdHome
        ON dbo.MatchHistory(StandardizedHomeTeam, MatchDate DESC, Id DESC)
        INCLUDE
        (
            HomeTeamGender, AwayTeamGender, League, StandardizedLeague, Season,
            HomeTeam, AwayTeam, StandardizedAwayTeam, HomeGoals, AwayGoals,
            HomeCorners, AwayCorners, HomeShots, AwayShots, HomeShotsOnGoal,
            AwayShotsOnGoal, HomePossession, AwayPossession, IsKnockout,
            HomeFormation, AwayFormation, CreatedAtUtc, UpdatedAtUtc, ApiFootballFixtureId
        )
        WHERE HomeCorners IS NOT NULL AND AwayCorners IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_AsOf_StdAway'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_AsOf_StdAway
        ON dbo.MatchHistory(StandardizedAwayTeam, MatchDate DESC, Id DESC)
        INCLUDE
        (
            HomeTeamGender, AwayTeamGender, League, StandardizedLeague, Season,
            HomeTeam, AwayTeam, StandardizedHomeTeam, HomeGoals, AwayGoals,
            HomeCorners, AwayCorners, HomeShots, AwayShots, HomeShotsOnGoal,
            AwayShotsOnGoal, HomePossession, AwayPossession, IsKnockout,
            HomeFormation, AwayFormation, CreatedAtUtc, UpdatedAtUtc, ApiFootballFixtureId
        )
        WHERE HomeCorners IS NOT NULL AND AwayCorners IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_AsOf_RawHome'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_AsOf_RawHome
        ON dbo.MatchHistory(HomeTeam, MatchDate DESC, Id DESC)
        INCLUDE
        (
            HomeTeamGender, AwayTeamGender, League, StandardizedLeague, Season,
            AwayTeam, StandardizedHomeTeam, StandardizedAwayTeam, HomeGoals, AwayGoals,
            HomeCorners, AwayCorners, HomeShots, AwayShots, HomeShotsOnGoal,
            AwayShotsOnGoal, HomePossession, AwayPossession, IsKnockout,
            HomeFormation, AwayFormation, CreatedAtUtc, UpdatedAtUtc, ApiFootballFixtureId
        )
        WHERE HomeCorners IS NOT NULL AND AwayCorners IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.MatchHistory', N'U')
          AND name = N'IX_MatchHistory_AsOf_RawAway'
    )
    BEGIN
        CREATE INDEX IX_MatchHistory_AsOf_RawAway
        ON dbo.MatchHistory(AwayTeam, MatchDate DESC, Id DESC)
        INCLUDE
        (
            HomeTeamGender, AwayTeamGender, League, StandardizedLeague, Season,
            HomeTeam, StandardizedHomeTeam, StandardizedAwayTeam, HomeGoals, AwayGoals,
            HomeCorners, AwayCorners, HomeShots, AwayShots, HomeShotsOnGoal,
            AwayShotsOnGoal, HomePossession, AwayPossession, IsKnockout,
            HomeFormation, AwayFormation, CreatedAtUtc, UpdatedAtUtc, ApiFootballFixtureId
        )
        WHERE HomeCorners IS NOT NULL AND AwayCorners IS NOT NULL;
    END;
END;
