CREATE OR ALTER PROCEDURE dbo.sp_GetMatchHistoryByTeams
    @HomeTeam NVARCHAR(150),
    @AwayTeam NVARCHAR(150),
    @League NVARCHAR(100) = NULL,
    @TeamGender CHAR(1) = 'M'
AS
BEGIN
    SET NOCOUNT ON;

    SET @HomeTeam = LTRIM(RTRIM(@HomeTeam));
    SET @AwayTeam = LTRIM(RTRIM(@AwayTeam));
    SET @League = NULLIF(LTRIM(RTRIM(@League)), '');
    SET @TeamGender = UPPER(COALESCE(NULLIF(@TeamGender, ''), 'M'));

    IF @TeamGender NOT IN ('M', 'F', 'U')
    BEGIN
        THROW 50001, 'TeamGender must be M, F or U.', 1;
    END;

    DECLARE @HomeStandard NVARCHAR(150) =
    (
        SELECT TOP (1) tm.StandardizedTeam
        FROM dbo.TeamMapping tm
        WHERE tm.SourceTeam = @HomeTeam
           OR tm.StandardizedTeam = @HomeTeam
        ORDER BY
            CASE WHEN @League IS NOT NULL AND tm.League = @League THEN 0 ELSE 1 END,
            tm.StandardizedTeam
    );

    DECLARE @AwayStandard NVARCHAR(150) =
    (
        SELECT TOP (1) tm.StandardizedTeam
        FROM dbo.TeamMapping tm
        WHERE tm.SourceTeam = @AwayTeam
           OR tm.StandardizedTeam = @AwayTeam
        ORDER BY
            CASE WHEN @League IS NOT NULL AND tm.League = @League THEN 0 ELSE 1 END,
            tm.StandardizedTeam
    );

    SET @HomeStandard = COALESCE(@HomeStandard, @HomeTeam);
    SET @AwayStandard = COALESCE(@AwayStandard, @AwayTeam);

    ;WITH HomeAliases AS
    (
        SELECT TeamName = @HomeTeam
        UNION
        SELECT TeamName = @HomeStandard
        UNION
        SELECT tm.SourceTeam
        FROM dbo.TeamMapping tm
        WHERE tm.StandardizedTeam = @HomeStandard
           OR tm.SourceTeam = @HomeTeam
           OR tm.StandardizedTeam = @HomeTeam
        UNION
        SELECT tm.StandardizedTeam
        FROM dbo.TeamMapping tm
        WHERE tm.StandardizedTeam = @HomeStandard
           OR tm.SourceTeam = @HomeTeam
           OR tm.StandardizedTeam = @HomeTeam
    ),
    AwayAliases AS
    (
        SELECT TeamName = @AwayTeam
        UNION
        SELECT TeamName = @AwayStandard
        UNION
        SELECT tm.SourceTeam
        FROM dbo.TeamMapping tm
        WHERE tm.StandardizedTeam = @AwayStandard
           OR tm.SourceTeam = @AwayTeam
           OR tm.StandardizedTeam = @AwayTeam
        UNION
        SELECT tm.StandardizedTeam
        FROM dbo.TeamMapping tm
        WHERE tm.StandardizedTeam = @AwayStandard
           OR tm.SourceTeam = @AwayTeam
           OR tm.StandardizedTeam = @AwayTeam
    ),
    CandidateMatches AS
    (
        SELECT
            EquipoCondicion = CAST('HOME' AS NVARCHAR(10)),
            CondicionReal = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN CAST('LOCAL' AS NVARCHAR(10))
                ELSE CAST('VISITA' AS NVARCHAR(10))
            END,
            mh.Id,
            mh.League,
            mh.Season,
            mh.MatchDate,
            Equipo = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeTeam
                ELSE mh.AwayTeam
            END,
            Rival = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayTeam
                ELSE mh.HomeTeam
            END,
            GolesEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeGoals
                ELSE mh.AwayGoals
            END,
            GolesRival = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayGoals
                ELSE mh.HomeGoals
            END,
            CornersEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeCorners
                ELSE mh.AwayCorners
            END,
            CornersRival = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayCorners
                ELSE mh.HomeCorners
            END,
            TirosEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeShots
                ELSE mh.AwayShots
            END,
            TirosRival = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayShots
                ELSE mh.HomeShots
            END,
            TirosPuertaEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeShotsOnGoal
                ELSE mh.AwayShotsOnGoal
            END,
            TirosPuertaRival = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayShotsOnGoal
                ELSE mh.HomeShotsOnGoal
            END,
            PosesionEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomePossession
                ELSE mh.AwayPossession
            END,
            PosesionRival = CASE
                WHEN EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayPossession
                ELSE mh.HomePossession
            END,
            mh.IsKnockout,
            mh.HomeFormation,
            mh.AwayFormation,
            mh.CreatedAtUtc,
            mh.UpdatedAtUtc
        FROM dbo.MatchHistory mh
        WHERE mh.HomeTeamGender = @TeamGender
          AND mh.AwayTeamGender = @TeamGender
          AND
          (
              EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.HomeTeam)
              OR EXISTS (SELECT 1 FROM HomeAliases a WHERE a.TeamName = mh.AwayTeam)
          )

        UNION ALL

        SELECT
            EquipoCondicion = CAST('AWAY' AS NVARCHAR(10)),
            CondicionReal = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN CAST('LOCAL' AS NVARCHAR(10))
                ELSE CAST('VISITA' AS NVARCHAR(10))
            END,
            mh.Id,
            mh.League,
            mh.Season,
            mh.MatchDate,
            Equipo = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeTeam
                ELSE mh.AwayTeam
            END,
            Rival = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayTeam
                ELSE mh.HomeTeam
            END,
            GolesEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeGoals
                ELSE mh.AwayGoals
            END,
            GolesRival = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayGoals
                ELSE mh.HomeGoals
            END,
            CornersEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeCorners
                ELSE mh.AwayCorners
            END,
            CornersRival = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayCorners
                ELSE mh.HomeCorners
            END,
            TirosEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeShots
                ELSE mh.AwayShots
            END,
            TirosRival = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayShots
                ELSE mh.HomeShots
            END,
            TirosPuertaEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomeShotsOnGoal
                ELSE mh.AwayShotsOnGoal
            END,
            TirosPuertaRival = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayShotsOnGoal
                ELSE mh.HomeShotsOnGoal
            END,
            PosesionEquipo = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.HomePossession
                ELSE mh.AwayPossession
            END,
            PosesionRival = CASE
                WHEN EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam) THEN mh.AwayPossession
                ELSE mh.HomePossession
            END,
            mh.IsKnockout,
            mh.HomeFormation,
            mh.AwayFormation,
            mh.CreatedAtUtc,
            mh.UpdatedAtUtc
        FROM dbo.MatchHistory mh
        WHERE mh.HomeTeamGender = @TeamGender
          AND mh.AwayTeamGender = @TeamGender
          AND
          (
              EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.HomeTeam)
              OR EXISTS (SELECT 1 FROM AwayAliases a WHERE a.TeamName = mh.AwayTeam)
          )
    ),
    HistoryBuckets AS
    (
        SELECT
            TipoHistorial = CAST('ULTIMOS_10_GENERAL' AS NVARCHAR(30)),
            RnHistorial = ROW_NUMBER() OVER (
                PARTITION BY EquipoCondicion
                ORDER BY MatchDate DESC, Id DESC),
            *
        FROM CandidateMatches

        UNION ALL

        SELECT
            TipoHistorial = CAST('ULTIMOS_10_LOCAL' AS NVARCHAR(30)),
            RnHistorial = ROW_NUMBER() OVER (
                PARTITION BY EquipoCondicion
                ORDER BY MatchDate DESC, Id DESC),
            *
        FROM CandidateMatches
        WHERE CondicionReal = 'LOCAL'

        UNION ALL

        SELECT
            TipoHistorial = CAST('ULTIMOS_10_VISITA' AS NVARCHAR(30)),
            RnHistorial = ROW_NUMBER() OVER (
                PARTITION BY EquipoCondicion
                ORDER BY MatchDate DESC, Id DESC),
            *
        FROM CandidateMatches
        WHERE CondicionReal = 'VISITA'
    )
    SELECT
        TipoHistorial,
        RnHistorial,
        EquipoCondicion,
        CondicionReal,
        Id,
        League,
        Season,
        MatchDate,
        Equipo,
        Rival,
        GolesEquipo,
        GolesRival,
        CornersEquipo,
        CornersRival,
        TirosEquipo,
        TirosRival,
        TirosPuertaEquipo,
        TirosPuertaRival,
        PosesionEquipo,
        PosesionRival,
        IsKnockout,
        HomeFormation,
        AwayFormation,
        CreatedAtUtc,
        UpdatedAtUtc
    FROM HistoryBuckets
    WHERE RnHistorial <= 10
    ORDER BY
        EquipoCondicion,
        CASE TipoHistorial
            WHEN 'ULTIMOS_10_GENERAL' THEN 0
            WHEN 'ULTIMOS_10_LOCAL' THEN 1
            WHEN 'ULTIMOS_10_VISITA' THEN 2
            ELSE 3
        END,
        RnHistorial;
END
GO
