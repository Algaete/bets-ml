CREATE OR ALTER PROCEDURE dbo.sp_BulkInsertMatchHistoryJson
    @League NVARCHAR(200),
    @Season NVARCHAR(50),
    @FocusTeam NVARCHAR(150),
    @TeamGender CHAR(1) = 'M',
    @IsKnockout BIT = 0,
    @MatchesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @LeagueClean NVARCHAR(200) = NULLIF(LTRIM(RTRIM(@League)), '');
    DECLARE @SeasonClean NVARCHAR(50) = NULLIF(LTRIM(RTRIM(@Season)), '');
    DECLARE @FocusTeamClean NVARCHAR(150) = NULLIF(LTRIM(RTRIM(@FocusTeam)), '');
    SET @TeamGender = UPPER(COALESCE(NULLIF(@TeamGender, ''), 'M'));

    DECLARE @Results TABLE
    (
        RowNumber INT NOT NULL,
        MatchDate DATE NULL,
        HomeTeam NVARCHAR(150) NULL,
        AwayTeam NVARCHAR(150) NULL,
        Status NVARCHAR(20) NOT NULL,
        Message NVARCHAR(4000) NOT NULL,
        InsertedId BIGINT NULL
    );

    IF @LeagueClean IS NULL
    BEGIN
        INSERT INTO @Results VALUES (0, NULL, NULL, NULL, 'Error', 'League is required.', NULL);
        SELECT * FROM @Results ORDER BY RowNumber;
        RETURN;
    END;

    IF @SeasonClean IS NULL
    BEGIN
        INSERT INTO @Results VALUES (0, NULL, NULL, NULL, 'Error', 'Season is required.', NULL);
        SELECT * FROM @Results ORDER BY RowNumber;
        RETURN;
    END;

    IF @FocusTeamClean IS NULL
    BEGIN
        INSERT INTO @Results VALUES (0, NULL, NULL, NULL, 'Error', 'FocusTeam is required.', NULL);
        SELECT * FROM @Results ORDER BY RowNumber;
        RETURN;
    END;

    IF @TeamGender NOT IN ('M', 'F', 'U')
    BEGIN
        INSERT INTO @Results VALUES (0, NULL, NULL, NULL, 'Error', 'TeamGender must be M, F or U.', NULL);
        SELECT * FROM @Results ORDER BY RowNumber;
        RETURN;
    END;

    IF @MatchesJson IS NULL OR ISJSON(@MatchesJson) <> 1
    BEGIN
        INSERT INTO @Results VALUES (0, NULL, NULL, NULL, 'Error', 'MatchesJson must be valid JSON.', NULL);
        SELECT * FROM @Results ORDER BY RowNumber;
        RETURN;
    END;

    DECLARE @KnownLeagues TABLE
    (
        LeagueName NVARCHAR(200) NOT NULL PRIMARY KEY
    );

    INSERT INTO @KnownLeagues (LeagueName)
    SELECT LeagueName
    FROM
    (
        SELECT LeagueName = NULLIF(LTRIM(RTRIM(SourceLeague)), '') FROM dbo.LeagueMapping
        UNION
        SELECT LeagueName = NULLIF(LTRIM(RTRIM(StandardizedLeague)), '') FROM dbo.LeagueMapping
        UNION
        SELECT LeagueName = NULLIF(LTRIM(RTRIM(SourceLeagueClean)), '') FROM dbo.LeagueMapping
        UNION
        SELECT LeagueName = NULLIF(LTRIM(RTRIM(StandardizedLeagueClean)), '') FROM dbo.LeagueMapping
        UNION
        SELECT LeagueName = NULLIF(LTRIM(RTRIM(League)), '') FROM dbo.MatchHistory
        UNION
        SELECT LeagueName = NULLIF(LTRIM(RTRIM(StandardizedLeague)), '') FROM dbo.MatchHistory
    ) leagues
    WHERE LeagueName IS NOT NULL
    GROUP BY LeagueName;

    IF NOT EXISTS (SELECT 1 FROM @KnownLeagues WHERE LeagueName = @LeagueClean)
    BEGIN
        DECLARE @SimilarLeagues NVARCHAR(1000);

        SELECT @SimilarLeagues = STRING_AGG(LeagueName, ', ')
        FROM
        (
            SELECT TOP (5) LeagueName
            FROM @KnownLeagues
            WHERE LeagueName LIKE '%' + @LeagueClean + '%'
               OR @LeagueClean LIKE '%' + LeagueName + '%'
               OR SOUNDEX(LeagueName) = SOUNDEX(@LeagueClean)
            ORDER BY
                CASE
                    WHEN LeagueName = @LeagueClean THEN 0
                    WHEN LeagueName LIKE @LeagueClean + '%' THEN 1
                    WHEN LeagueName LIKE '%' + @LeagueClean + '%' THEN 2
                    ELSE 3
                END,
                LeagueName
        ) suggestions;

        INSERT INTO @Results
        VALUES
        (
            0,
            NULL,
            NULL,
            NULL,
            'Error',
            CONCAT('League was not found in LeagueMapping or MatchHistory.', COALESCE(' Similar leagues: ' + @SimilarLeagues, '')),
            NULL
        );

        SELECT * FROM @Results ORDER BY RowNumber;
        RETURN;
    END;

    DECLARE @FocusTeamExists BIT = 0;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.TeamMapping tm
        WHERE tm.TeamGender = @TeamGender
          AND (
                tm.SourceTeamClean = @FocusTeamClean
                OR tm.StandardizedTeamClean = @FocusTeamClean
                OR LTRIM(RTRIM(tm.SourceTeam)) = @FocusTeamClean
                OR LTRIM(RTRIM(tm.StandardizedTeam)) = @FocusTeamClean
              )
          AND (
                tm.LeagueClean = @LeagueClean
                OR LTRIM(RTRIM(tm.League)) = @LeagueClean
              )
    )
    OR EXISTS
    (
        SELECT 1
        FROM dbo.MatchHistory mh
        WHERE (mh.HomeTeamGender = @TeamGender OR mh.AwayTeamGender = @TeamGender)
          AND (mh.League = @LeagueClean OR mh.StandardizedLeague = @LeagueClean)
          AND (
                mh.HomeTeam = @FocusTeamClean
                OR mh.AwayTeam = @FocusTeamClean
                OR mh.StandardizedHomeTeam = @FocusTeamClean
                OR mh.StandardizedAwayTeam = @FocusTeamClean
              )
    )
    BEGIN
        SET @FocusTeamExists = 1;
    END;

    IF @FocusTeamExists = 0
    BEGIN
        INSERT INTO @Results
        VALUES (0, NULL, NULL, NULL, 'Error', 'FocusTeam was not found for the selected league and gender.', NULL);
        SELECT * FROM @Results ORDER BY RowNumber;
        RETURN;
    END;

    DECLARE @FocusAliases TABLE
    (
        TeamName NVARCHAR(150) NOT NULL PRIMARY KEY
    );

    INSERT INTO @FocusAliases (TeamName)
    SELECT TeamName
    FROM
    (
        SELECT TeamName = @FocusTeamClean
        UNION
        SELECT TeamName = NULLIF(LTRIM(RTRIM(tm.SourceTeam)), '')
        FROM dbo.TeamMapping tm
        WHERE tm.TeamGender = @TeamGender
          AND (
                tm.SourceTeamClean = @FocusTeamClean
                OR tm.StandardizedTeamClean = @FocusTeamClean
                OR LTRIM(RTRIM(tm.SourceTeam)) = @FocusTeamClean
                OR LTRIM(RTRIM(tm.StandardizedTeam)) = @FocusTeamClean
              )
          AND (tm.LeagueClean = @LeagueClean OR LTRIM(RTRIM(tm.League)) = @LeagueClean)
        UNION
        SELECT TeamName = NULLIF(LTRIM(RTRIM(tm.StandardizedTeam)), '')
        FROM dbo.TeamMapping tm
        WHERE tm.TeamGender = @TeamGender
          AND (
                tm.SourceTeamClean = @FocusTeamClean
                OR tm.StandardizedTeamClean = @FocusTeamClean
                OR LTRIM(RTRIM(tm.SourceTeam)) = @FocusTeamClean
                OR LTRIM(RTRIM(tm.StandardizedTeam)) = @FocusTeamClean
              )
          AND (tm.LeagueClean = @LeagueClean OR LTRIM(RTRIM(tm.League)) = @LeagueClean)
        UNION
        SELECT TeamName = NULLIF(LTRIM(RTRIM(mh.HomeTeam)), '')
        FROM dbo.MatchHistory mh
        WHERE (mh.League = @LeagueClean OR mh.StandardizedLeague = @LeagueClean)
          AND (mh.HomeTeam = @FocusTeamClean OR mh.StandardizedHomeTeam = @FocusTeamClean)
        UNION
        SELECT TeamName = NULLIF(LTRIM(RTRIM(mh.AwayTeam)), '')
        FROM dbo.MatchHistory mh
        WHERE (mh.League = @LeagueClean OR mh.StandardizedLeague = @LeagueClean)
          AND (mh.AwayTeam = @FocusTeamClean OR mh.StandardizedAwayTeam = @FocusTeamClean)
        UNION
        SELECT TeamName = NULLIF(LTRIM(RTRIM(mh.StandardizedHomeTeam)), '')
        FROM dbo.MatchHistory mh
        WHERE (mh.League = @LeagueClean OR mh.StandardizedLeague = @LeagueClean)
          AND (mh.HomeTeam = @FocusTeamClean OR mh.StandardizedHomeTeam = @FocusTeamClean)
        UNION
        SELECT TeamName = NULLIF(LTRIM(RTRIM(mh.StandardizedAwayTeam)), '')
        FROM dbo.MatchHistory mh
        WHERE (mh.League = @LeagueClean OR mh.StandardizedLeague = @LeagueClean)
          AND (mh.AwayTeam = @FocusTeamClean OR mh.StandardizedAwayTeam = @FocusTeamClean)
    ) aliases
    WHERE TeamName IS NOT NULL
    GROUP BY TeamName;

    DECLARE @Rows TABLE
    (
        RowNumber INT NOT NULL PRIMARY KEY,
        MatchDate DATE NULL,
        HomeTeam NVARCHAR(150) NULL,
        AwayTeam NVARCHAR(150) NULL,
        HomeFormation NVARCHAR(50) NULL,
        AwayFormation NVARCHAR(50) NULL,
        HomeGoals INT NULL,
        AwayGoals INT NULL,
        HomeCorners INT NULL,
        AwayCorners INT NULL,
        HomeShots INT NULL,
        AwayShots INT NULL,
        HomeShotsOnGoal INT NULL,
        AwayShotsOnGoal INT NULL,
        HomePossession DECIMAL(5,2) NULL,
        AwayPossession DECIMAL(5,2) NULL,
        SourceMatchId NVARCHAR(100) NULL
    );

    INSERT INTO @Rows
    (
        RowNumber,
        MatchDate,
        HomeTeam,
        AwayTeam,
        HomeFormation,
        AwayFormation,
        HomeGoals,
        AwayGoals,
        HomeCorners,
        AwayCorners,
        HomeShots,
        AwayShots,
        HomeShotsOnGoal,
        AwayShotsOnGoal,
        HomePossession,
        AwayPossession,
        SourceMatchId
    )
    SELECT
        RowNumber = TRY_CONVERT(INT, jsonRows.[key]) + 1,
        MatchDate = TRY_CONVERT(DATE, parsed.MatchDateText, 23),
        HomeTeam = NULLIF(LTRIM(RTRIM(parsed.HomeTeam)), ''),
        AwayTeam = NULLIF(LTRIM(RTRIM(parsed.AwayTeam)), ''),
        HomeFormation = NULLIF(LTRIM(RTRIM(parsed.HomeFormation)), ''),
        AwayFormation = NULLIF(LTRIM(RTRIM(parsed.AwayFormation)), ''),
        parsed.HomeGoals,
        parsed.AwayGoals,
        parsed.HomeCorners,
        parsed.AwayCorners,
        parsed.HomeShots,
        parsed.AwayShots,
        parsed.HomeShotsOnGoal,
        parsed.AwayShotsOnGoal,
        parsed.HomePossession,
        parsed.AwayPossession,
        NULLIF(LTRIM(RTRIM(parsed.SourceMatchId)), '')
    FROM OPENJSON(@MatchesJson) jsonRows
    CROSS APPLY OPENJSON(jsonRows.value)
    WITH
    (
        MatchDateText NVARCHAR(30) '$.matchDate',
        HomeTeam NVARCHAR(150) '$.homeTeam',
        AwayTeam NVARCHAR(150) '$.awayTeam',
        HomeFormation NVARCHAR(50) '$.homeFormation',
        AwayFormation NVARCHAR(50) '$.awayFormation',
        HomeGoals INT '$.homeGoals',
        AwayGoals INT '$.awayGoals',
        HomeCorners INT '$.homeCorners',
        AwayCorners INT '$.awayCorners',
        HomeShots INT '$.homeShots',
        AwayShots INT '$.awayShots',
        HomeShotsOnGoal INT '$.homeShotsOnGoal',
        AwayShotsOnGoal INT '$.awayShotsOnGoal',
        HomePossession DECIMAL(5,2) '$.homePossession',
        AwayPossession DECIMAL(5,2) '$.awayPossession',
        SourceMatchId NVARCHAR(100) '$.sourceMatchId'
    ) parsed;

    IF NOT EXISTS (SELECT 1 FROM @Rows)
    BEGIN
        INSERT INTO @Results VALUES (0, NULL, NULL, NULL, 'Error', 'MatchesJson must contain at least one match.', NULL);
        SELECT * FROM @Results ORDER BY RowNumber;
        RETURN;
    END;

    DECLARE
        @RowNumber INT,
        @MatchDate DATE,
        @HomeTeam NVARCHAR(150),
        @AwayTeam NVARCHAR(150),
        @HomeFormation NVARCHAR(50),
        @AwayFormation NVARCHAR(50),
        @HomeGoals INT,
        @AwayGoals INT,
        @HomeCorners INT,
        @AwayCorners INT,
        @HomeShots INT,
        @AwayShots INT,
        @HomeShotsOnGoal INT,
        @AwayShotsOnGoal INT,
        @HomePossession DECIMAL(5,2),
        @AwayPossession DECIMAL(5,2),
        @SourceMatchId NVARCHAR(100);

    DECLARE row_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT
            RowNumber,
            MatchDate,
            HomeTeam,
            AwayTeam,
            HomeFormation,
            AwayFormation,
            HomeGoals,
            AwayGoals,
            HomeCorners,
            AwayCorners,
            HomeShots,
            AwayShots,
            HomeShotsOnGoal,
            AwayShotsOnGoal,
            HomePossession,
            AwayPossession,
            SourceMatchId
        FROM @Rows
        ORDER BY RowNumber;

    OPEN row_cursor;

    FETCH NEXT FROM row_cursor INTO
        @RowNumber,
        @MatchDate,
        @HomeTeam,
        @AwayTeam,
        @HomeFormation,
        @AwayFormation,
        @HomeGoals,
        @AwayGoals,
        @HomeCorners,
        @AwayCorners,
        @HomeShots,
        @AwayShots,
        @HomeShotsOnGoal,
        @AwayShotsOnGoal,
        @HomePossession,
        @AwayPossession,
        @SourceMatchId;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @MatchDate IS NULL
        BEGIN
            INSERT INTO @Results VALUES (@RowNumber, @MatchDate, @HomeTeam, @AwayTeam, 'Error', 'matchDate is required and must use yyyy-MM-dd.', NULL);
        END
        ELSE IF @HomeTeam IS NULL OR @AwayTeam IS NULL
        BEGIN
            INSERT INTO @Results VALUES (@RowNumber, @MatchDate, @HomeTeam, @AwayTeam, 'Error', 'homeTeam and awayTeam are required.', NULL);
        END
        ELSE IF @HomeTeam = @AwayTeam
        BEGIN
            INSERT INTO @Results VALUES (@RowNumber, @MatchDate, @HomeTeam, @AwayTeam, 'Error', 'homeTeam and awayTeam must be different.', NULL);
        END
        ELSE IF NOT EXISTS (SELECT 1 FROM @FocusAliases WHERE TeamName IN (@HomeTeam, @AwayTeam))
        BEGIN
            INSERT INTO @Results VALUES (@RowNumber, @MatchDate, @HomeTeam, @AwayTeam, 'Error', 'The selected team is not present in this match.', NULL);
        END
        ELSE IF @HomeGoals IS NULL
             OR @AwayGoals IS NULL
             OR @HomeCorners IS NULL
             OR @AwayCorners IS NULL
             OR @HomeShots IS NULL
             OR @AwayShots IS NULL
             OR @HomeShotsOnGoal IS NULL
             OR @AwayShotsOnGoal IS NULL
             OR @HomePossession IS NULL
             OR @AwayPossession IS NULL
        BEGIN
            INSERT INTO @Results VALUES (@RowNumber, @MatchDate, @HomeTeam, @AwayTeam, 'Error', 'All numeric stats are required.', NULL);
        END
        ELSE
        BEGIN
            BEGIN TRY
                DECLARE @InsertedId BIGINT = NULL;

                EXEC dbo.sp_InsertMatchHistory
                    @League = @LeagueClean,
                    @Season = @SeasonClean,
                    @MatchDate = @MatchDate,
                    @HomeTeam = @HomeTeam,
                    @AwayTeam = @AwayTeam,
                    @HomeFormation = @HomeFormation,
                    @AwayFormation = @AwayFormation,
                    @HomeGoals = @HomeGoals,
                    @AwayGoals = @AwayGoals,
                    @HomeCorners = @HomeCorners,
                    @AwayCorners = @AwayCorners,
                    @HomeShots = @HomeShots,
                    @AwayShots = @AwayShots,
                    @HomeShotsOnGoal = @HomeShotsOnGoal,
                    @AwayShotsOnGoal = @AwayShotsOnGoal,
                    @HomePossession = @HomePossession,
                    @AwayPossession = @AwayPossession,
                    @IsKnockout = @IsKnockout,
                    @SourceMatchId = @SourceMatchId,
                    @HomeTeamGender = @TeamGender,
                    @AwayTeamGender = @TeamGender,
                    @TotalTeams = NULL,
                    @HomeTeamPosition = NULL,
                    @AwayTeamPosition = NULL,
                    @InsertedId = @InsertedId OUTPUT;

                INSERT INTO @Results VALUES (@RowNumber, @MatchDate, @HomeTeam, @AwayTeam, 'Inserted', 'Match inserted.', @InsertedId);
            END TRY
            BEGIN CATCH
                DECLARE @ErrorNumber INT = ERROR_NUMBER();
                DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();

                INSERT INTO @Results
                VALUES
                (
                    @RowNumber,
                    @MatchDate,
                    @HomeTeam,
                    @AwayTeam,
                    CASE WHEN @ErrorNumber = 50018 THEN 'Duplicate' ELSE 'Error' END,
                    @ErrorMessage,
                    NULL
                );
            END CATCH;
        END;

        FETCH NEXT FROM row_cursor INTO
            @RowNumber,
            @MatchDate,
            @HomeTeam,
            @AwayTeam,
            @HomeFormation,
            @AwayFormation,
            @HomeGoals,
            @AwayGoals,
            @HomeCorners,
            @AwayCorners,
            @HomeShots,
            @AwayShots,
            @HomeShotsOnGoal,
            @AwayShotsOnGoal,
            @HomePossession,
            @AwayPossession,
            @SourceMatchId;
    END;

    CLOSE row_cursor;
    DEALLOCATE row_cursor;

    SELECT
        RowNumber,
        MatchDate,
        HomeTeam,
        AwayTeam,
        Status,
        Message,
        InsertedId
    FROM @Results
    ORDER BY RowNumber;
END;

