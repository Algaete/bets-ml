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

    SET @HomeStandard = dbo.fn_CanonicalTeamName(COALESCE(@HomeStandard, @HomeTeam));
    SET @AwayStandard = dbo.fn_CanonicalTeamName(COALESCE(@AwayStandard, @AwayTeam));

    ;WITH NormalizedMatches AS
    (
        SELECT
            mh.Id,
            League = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedLeague)), ''), mh.League),
            mh.Season,
            mh.MatchDate,
            HomeTeam = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedHomeTeam)), ''), mh.HomeTeam),
            AwayTeam = COALESCE(NULLIF(LTRIM(RTRIM(mh.StandardizedAwayTeam)), ''), mh.AwayTeam),
            mh.HomeGoals,
            mh.AwayGoals,
            mh.HomeCorners,
            mh.AwayCorners,
            mh.HomeShots,
            mh.AwayShots,
            mh.HomeShotsOnGoal,
            mh.AwayShotsOnGoal,
            mh.HomePossession,
            mh.AwayPossession,
            mh.IsKnockout,
            mh.HomeFormation,
            mh.AwayFormation,
            mh.CreatedAtUtc,
            mh.UpdatedAtUtc
        FROM dbo.MatchHistory mh
        WHERE mh.HomeTeamGender = @TeamGender
          AND mh.AwayTeamGender = @TeamGender
    ),
    CandidateMatches AS
    (
        SELECT
            EquipoCondicion = CAST('HOME' AS NVARCHAR(10)),
            CondicionReal = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN CAST('LOCAL' AS NVARCHAR(10))
                ELSE CAST('VISITA' AS NVARCHAR(10))
            END,
            nm.Id,
            nm.League,
            nm.Season,
            nm.MatchDate,
            Equipo = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.HomeTeam
                ELSE nm.AwayTeam
            END,
            Rival = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.AwayTeam
                ELSE nm.HomeTeam
            END,
            GolesEquipo = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.HomeGoals
                ELSE nm.AwayGoals
            END,
            GolesRival = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.AwayGoals
                ELSE nm.HomeGoals
            END,
            CornersEquipo = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.HomeCorners
                ELSE nm.AwayCorners
            END,
            CornersRival = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.AwayCorners
                ELSE nm.HomeCorners
            END,
            TirosEquipo = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.HomeShots
                ELSE nm.AwayShots
            END,
            TirosRival = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.AwayShots
                ELSE nm.HomeShots
            END,
            TirosPuertaEquipo = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.HomeShotsOnGoal
                ELSE nm.AwayShotsOnGoal
            END,
            TirosPuertaRival = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.AwayShotsOnGoal
                ELSE nm.HomeShotsOnGoal
            END,
            PosesionEquipo = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.HomePossession
                ELSE nm.AwayPossession
            END,
            PosesionRival = CASE
                WHEN nm.HomeTeam = @HomeStandard THEN nm.AwayPossession
                ELSE nm.HomePossession
            END,
            nm.IsKnockout,
            nm.HomeFormation,
            nm.AwayFormation,
            nm.CreatedAtUtc,
            nm.UpdatedAtUtc
        FROM NormalizedMatches nm
        WHERE nm.HomeTeam = @HomeStandard
           OR nm.AwayTeam = @HomeStandard

        UNION ALL

        SELECT
            EquipoCondicion = CAST('AWAY' AS NVARCHAR(10)),
            CondicionReal = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN CAST('LOCAL' AS NVARCHAR(10))
                ELSE CAST('VISITA' AS NVARCHAR(10))
            END,
            nm.Id,
            nm.League,
            nm.Season,
            nm.MatchDate,
            Equipo = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.HomeTeam
                ELSE nm.AwayTeam
            END,
            Rival = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.AwayTeam
                ELSE nm.HomeTeam
            END,
            GolesEquipo = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.HomeGoals
                ELSE nm.AwayGoals
            END,
            GolesRival = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.AwayGoals
                ELSE nm.HomeGoals
            END,
            CornersEquipo = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.HomeCorners
                ELSE nm.AwayCorners
            END,
            CornersRival = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.AwayCorners
                ELSE nm.HomeCorners
            END,
            TirosEquipo = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.HomeShots
                ELSE nm.AwayShots
            END,
            TirosRival = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.AwayShots
                ELSE nm.HomeShots
            END,
            TirosPuertaEquipo = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.HomeShotsOnGoal
                ELSE nm.AwayShotsOnGoal
            END,
            TirosPuertaRival = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.AwayShotsOnGoal
                ELSE nm.HomeShotsOnGoal
            END,
            PosesionEquipo = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.HomePossession
                ELSE nm.AwayPossession
            END,
            PosesionRival = CASE
                WHEN nm.HomeTeam = @AwayStandard THEN nm.AwayPossession
                ELSE nm.HomePossession
            END,
            nm.IsKnockout,
            nm.HomeFormation,
            nm.AwayFormation,
            nm.CreatedAtUtc,
            nm.UpdatedAtUtc
        FROM NormalizedMatches nm
        WHERE nm.HomeTeam = @AwayStandard
           OR nm.AwayTeam = @AwayStandard
    ),
    HistoryBuckets AS
    (
        SELECT
            TipoHistorial = CAST('ULTIMOS_10_GENERAL' AS NVARCHAR(30)),
            RnHistorial = ROW_NUMBER() OVER (PARTITION BY EquipoCondicion ORDER BY MatchDate DESC, Id DESC),
            *
        FROM CandidateMatches

        UNION ALL

        SELECT
            TipoHistorial = CAST('ULTIMOS_10_LOCAL' AS NVARCHAR(30)),
            RnHistorial = ROW_NUMBER() OVER (PARTITION BY EquipoCondicion ORDER BY MatchDate DESC, Id DESC),
            *
        FROM CandidateMatches
        WHERE CondicionReal = 'LOCAL'

        UNION ALL

        SELECT
            TipoHistorial = CAST('ULTIMOS_10_VISITA' AS NVARCHAR(30)),
            RnHistorial = ROW_NUMBER() OVER (PARTITION BY EquipoCondicion ORDER BY MatchDate DESC, Id DESC),
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
