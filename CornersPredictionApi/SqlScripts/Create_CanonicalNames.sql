CREATE OR ALTER FUNCTION dbo.fn_NormalizeNameKey
(
    @Value NVARCHAR(250)
)
RETURNS NVARCHAR(250)
AS
BEGIN
    DECLARE @Key NVARCHAR(250) = LOWER(LTRIM(RTRIM(COALESCE(@Value, N''))));

    SET @Key = REPLACE(@Key, N'á', N'a');
    SET @Key = REPLACE(@Key, N'é', N'e');
    SET @Key = REPLACE(@Key, N'í', N'i');
    SET @Key = REPLACE(@Key, N'ó', N'o');
    SET @Key = REPLACE(@Key, N'ú', N'u');
    SET @Key = REPLACE(@Key, N'ü', N'u');
    SET @Key = REPLACE(@Key, N'ñ', N'n');
    SET @Key = REPLACE(@Key, N'-', N' ');
    SET @Key = REPLACE(@Key, N'.', N' ');
    SET @Key = REPLACE(@Key, N',', N' ');
    SET @Key = REPLACE(@Key, N'''', N' ');
    SET @Key = REPLACE(@Key, NCHAR(8217), N' ');
    SET @Key = REPLACE(@Key, N'(', N' ');
    SET @Key = REPLACE(@Key, N')', N' ');

    WHILE CHARINDEX(N'  ', @Key) > 0
        SET @Key = REPLACE(@Key, N'  ', N' ');

    RETURN LTRIM(RTRIM(@Key));
END;
GO

IF OBJECT_ID(N'dbo.TeamNameAlias', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TeamNameAlias
    (
        AliasKey NVARCHAR(250) NOT NULL
            CONSTRAINT PK_TeamNameAlias PRIMARY KEY,
        CanonicalName NVARCHAR(150) NOT NULL,
        UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_TeamNameAlias_UpdatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'dbo.LeagueNameAlias', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeagueNameAlias
    (
        AliasKey NVARCHAR(250) NOT NULL
            CONSTRAINT PK_LeagueNameAlias PRIMARY KEY,
        CanonicalName NVARCHAR(200) NOT NULL,
        UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_LeagueNameAlias_UpdatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'dbo.CanonicalNameNormalizationState', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CanonicalNameNormalizationState
    (
        StateId TINYINT NOT NULL
            CONSTRAINT PK_CanonicalNameNormalizationState PRIMARY KEY
            CONSTRAINT CK_CanonicalNameNormalizationState_StateId CHECK (StateId = 1),
        CatalogVersion NVARCHAR(50) NOT NULL,
        AppliedAtUtc DATETIME2(0) NOT NULL
    );
END;
GO

CREATE OR ALTER FUNCTION dbo.fn_CanonicalTeamName
(
    @Value NVARCHAR(150)
)
RETURNS NVARCHAR(150)
AS
BEGIN
    DECLARE @Clean NVARCHAR(150) = NULLIF(LTRIM(RTRIM(@Value)), N'');
    DECLARE @Canonical NVARCHAR(150);

    SELECT @Canonical = CanonicalName
    FROM dbo.TeamNameAlias
    WHERE AliasKey = dbo.fn_NormalizeNameKey(@Clean);

    RETURN COALESCE(@Canonical, @Clean);
END;
GO

CREATE OR ALTER FUNCTION dbo.fn_CanonicalLeagueName
(
    @Value NVARCHAR(200)
)
RETURNS NVARCHAR(200)
AS
BEGIN
    DECLARE @Clean NVARCHAR(200) = NULLIF(LTRIM(RTRIM(@Value)), N'');
    DECLARE @Canonical NVARCHAR(200);

    SELECT @Canonical = CanonicalName
    FROM dbo.LeagueNameAlias
    WHERE AliasKey = dbo.fn_NormalizeNameKey(@Clean);

    RETURN COALESCE(@Canonical, @Clean);
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_ApplyCanonicalNames
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.PartidosProximos', N'U') IS NOT NULL
    BEGIN
        ;WITH Duplicates AS
        (
            SELECT
                PartidoID,
                rn = ROW_NUMBER() OVER
                (
                    PARTITION BY
                        FechaPartido,
                        dbo.fn_CanonicalLeagueName(Liga),
                        dbo.fn_CanonicalTeamName(EquipoLocal),
                        dbo.fn_CanonicalTeamName(EquipoVisita),
                        Genero
                    ORDER BY FechaActualizacion DESC, PartidoID DESC
                )
            FROM dbo.PartidosProximos
        )
        DELETE pp
        FROM dbo.PartidosProximos pp
        INNER JOIN Duplicates d ON d.PartidoID = pp.PartidoID
        WHERE d.rn > 1;

        UPDATE pp
        SET
            Liga = COALESCE(leagueAlias.CanonicalName, pp.Liga),
            EquipoLocal = COALESCE(homeAlias.CanonicalName, pp.EquipoLocal),
            EquipoVisita = COALESCE(awayAlias.CanonicalName, pp.EquipoVisita),
            FechaActualizacion = SYSUTCDATETIME()
        FROM dbo.PartidosProximos pp
        LEFT JOIN dbo.LeagueNameAlias leagueAlias
            ON leagueAlias.AliasKey = dbo.fn_NormalizeNameKey(pp.Liga)
        LEFT JOIN dbo.TeamNameAlias homeAlias
            ON homeAlias.AliasKey = dbo.fn_NormalizeNameKey(pp.EquipoLocal)
        LEFT JOIN dbo.TeamNameAlias awayAlias
            ON awayAlias.AliasKey = dbo.fn_NormalizeNameKey(pp.EquipoVisita)
        WHERE pp.Liga <> COALESCE(leagueAlias.CanonicalName, pp.Liga)
           OR pp.EquipoLocal <> COALESCE(homeAlias.CanonicalName, pp.EquipoLocal)
           OR pp.EquipoVisita <> COALESCE(awayAlias.CanonicalName, pp.EquipoVisita);
    END;

    IF OBJECT_ID(N'dbo.PartidosProximosCuotas', N'U') IS NOT NULL
    BEGIN
        ;WITH Duplicates AS
        (
            SELECT
                PartidoProximoCuotaId,
                rn = ROW_NUMBER() OVER
                (
                    PARTITION BY
                        Source,
                        CAST(MatchDate AS DATE),
                        dbo.fn_CanonicalLeagueName(COALESCE(NULLIF(StandardizedLeague, N''), League)),
                        dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, N''), HomeTeam)),
                        dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, N''), AwayTeam)),
                        MarketType,
                        LineValue
                    ORDER BY UpdatedAtUtc DESC, PartidoProximoCuotaId DESC
                )
            FROM dbo.PartidosProximosCuotas
        )
        DELETE q
        FROM dbo.PartidosProximosCuotas q
        INNER JOIN Duplicates d ON d.PartidoProximoCuotaId = q.PartidoProximoCuotaId
        WHERE d.rn > 1;

        UPDATE q
        SET
            League = canonical.LeagueName,
            StandardizedLeague = canonical.LeagueName,
            HomeTeam = canonical.HomeTeamName,
            StandardizedHomeTeam = canonical.HomeTeamName,
            AwayTeam = canonical.AwayTeamName,
            StandardizedAwayTeam = canonical.AwayTeamName,
            UpdatedAtUtc = SYSUTCDATETIME()
        FROM dbo.PartidosProximosCuotas q
        CROSS APPLY
        (
            VALUES
            (
                COALESCE(NULLIF(q.StandardizedLeague, N''), q.League),
                COALESCE(NULLIF(q.StandardizedHomeTeam, N''), q.HomeTeam),
                COALESCE(NULLIF(q.StandardizedAwayTeam, N''), q.AwayTeam)
            )
        ) sourceNames(LeagueName, HomeTeamName, AwayTeamName)
        LEFT JOIN dbo.LeagueNameAlias leagueAlias
            ON leagueAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.LeagueName)
        LEFT JOIN dbo.TeamNameAlias homeAlias
            ON homeAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.HomeTeamName)
        LEFT JOIN dbo.TeamNameAlias awayAlias
            ON awayAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.AwayTeamName)
        CROSS APPLY
        (
            VALUES
            (
                COALESCE(leagueAlias.CanonicalName, sourceNames.LeagueName),
                COALESCE(homeAlias.CanonicalName, sourceNames.HomeTeamName),
                COALESCE(awayAlias.CanonicalName, sourceNames.AwayTeamName)
            )
        ) canonical(LeagueName, HomeTeamName, AwayTeamName)
        WHERE q.League <> canonical.LeagueName
           OR ISNULL(q.StandardizedLeague, N'') <> canonical.LeagueName
           OR q.HomeTeam <> canonical.HomeTeamName
           OR ISNULL(q.StandardizedHomeTeam, N'') <> canonical.HomeTeamName
           OR q.AwayTeam <> canonical.AwayTeamName
           OR ISNULL(q.StandardizedAwayTeam, N'') <> canonical.AwayTeamName;
    END;

    IF OBJECT_ID(N'dbo.MatchHistory', N'U') IS NOT NULL
    BEGIN
        ;WITH CanonicalDuplicates AS
        (
            SELECT
                Id,
                rn = ROW_NUMBER() OVER
                (
                    PARTITION BY
                        MatchDate,
                        dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, N''), HomeTeam)),
                        dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, N''), AwayTeam)),
                        ISNULL(NULLIF(HomeTeamGender, N''), N'M'),
                        ISNULL(NULLIF(AwayTeamGender, N''), N'M')
                    ORDER BY
                        CASE WHEN HomeCorners IS NOT NULL AND AwayCorners IS NOT NULL THEN 0 ELSE 1 END,
                        CASE WHEN HomeShots IS NOT NULL AND AwayShots IS NOT NULL THEN 0 ELSE 1 END,
                        UpdatedAtUtc DESC,
                        Id DESC
                )
            FROM dbo.MatchHistory
        )
        DELETE mh
        FROM dbo.MatchHistory mh
        INNER JOIN CanonicalDuplicates duplicate ON duplicate.Id = mh.Id
        WHERE duplicate.rn > 1;

        UPDATE mh
        SET
            League = canonical.LeagueName,
            StandardizedLeague = canonical.LeagueName,
            HomeTeam = canonical.HomeTeamName,
            StandardizedHomeTeam = canonical.HomeTeamName,
            AwayTeam = canonical.AwayTeamName,
            StandardizedAwayTeam = canonical.AwayTeamName,
            UpdatedAtUtc = SYSUTCDATETIME()
        FROM dbo.MatchHistory mh
        CROSS APPLY
        (
            VALUES
            (
                COALESCE(NULLIF(mh.StandardizedLeague, N''), mh.League),
                COALESCE(NULLIF(mh.StandardizedHomeTeam, N''), mh.HomeTeam),
                COALESCE(NULLIF(mh.StandardizedAwayTeam, N''), mh.AwayTeam)
            )
        ) sourceNames(LeagueName, HomeTeamName, AwayTeamName)
        LEFT JOIN dbo.LeagueNameAlias leagueAlias
            ON leagueAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.LeagueName)
        LEFT JOIN dbo.TeamNameAlias homeAlias
            ON homeAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.HomeTeamName)
        LEFT JOIN dbo.TeamNameAlias awayAlias
            ON awayAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.AwayTeamName)
        CROSS APPLY
        (
            VALUES
            (
                COALESCE(leagueAlias.CanonicalName, sourceNames.LeagueName),
                COALESCE(homeAlias.CanonicalName, sourceNames.HomeTeamName),
                COALESCE(awayAlias.CanonicalName, sourceNames.AwayTeamName)
            )
        ) canonical(LeagueName, HomeTeamName, AwayTeamName)
        WHERE mh.League <> canonical.LeagueName
           OR ISNULL(mh.StandardizedLeague, N'') <> canonical.LeagueName
           OR mh.HomeTeam <> canonical.HomeTeamName
           OR ISNULL(mh.StandardizedHomeTeam, N'') <> canonical.HomeTeamName
           OR mh.AwayTeam <> canonical.AwayTeamName
           OR ISNULL(mh.StandardizedAwayTeam, N'') <> canonical.AwayTeamName;
    END;

    IF OBJECT_ID(N'dbo.AutomatedCornerBetSelections', N'U') IS NOT NULL
    BEGIN
        ;WITH Duplicates AS
        (
            SELECT
                AutomatedCornerBetSelectionId,
                rn = ROW_NUMBER() OVER
                (
                    PARTITION BY
                        Source,
                        MarketType,
                        CAST(MatchDate AS DATE),
                        dbo.fn_CanonicalLeagueName(COALESCE(NULLIF(StandardizedLeague, N''), League)),
                        dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedHomeTeam, N''), HomeTeam)),
                        dbo.fn_CanonicalTeamName(COALESCE(NULLIF(StandardizedAwayTeam, N''), AwayTeam))
                    ORDER BY
                        CASE WHEN Status IN (N'Won', N'Lost', N'Void') THEN 0 ELSE 1 END,
                        UpdatedAtUtc DESC,
                        AutomatedCornerBetSelectionId DESC
                )
            FROM dbo.AutomatedCornerBetSelections
        )
        DELETE s
        FROM dbo.AutomatedCornerBetSelections s
        INNER JOIN Duplicates d ON d.AutomatedCornerBetSelectionId = s.AutomatedCornerBetSelectionId
        WHERE d.rn > 1;

        UPDATE s
        SET
            League = canonical.LeagueName,
            StandardizedLeague = canonical.LeagueName,
            HomeTeam = canonical.HomeTeamName,
            StandardizedHomeTeam = canonical.HomeTeamName,
            AwayTeam = canonical.AwayTeamName,
            StandardizedAwayTeam = canonical.AwayTeamName,
            UpdatedAtUtc = SYSUTCDATETIME()
        FROM dbo.AutomatedCornerBetSelections s
        CROSS APPLY
        (
            VALUES
            (
                COALESCE(NULLIF(s.StandardizedLeague, N''), s.League),
                COALESCE(NULLIF(s.StandardizedHomeTeam, N''), s.HomeTeam),
                COALESCE(NULLIF(s.StandardizedAwayTeam, N''), s.AwayTeam)
            )
        ) sourceNames(LeagueName, HomeTeamName, AwayTeamName)
        LEFT JOIN dbo.LeagueNameAlias leagueAlias
            ON leagueAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.LeagueName)
        LEFT JOIN dbo.TeamNameAlias homeAlias
            ON homeAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.HomeTeamName)
        LEFT JOIN dbo.TeamNameAlias awayAlias
            ON awayAlias.AliasKey = dbo.fn_NormalizeNameKey(sourceNames.AwayTeamName)
        CROSS APPLY
        (
            VALUES
            (
                COALESCE(leagueAlias.CanonicalName, sourceNames.LeagueName),
                COALESCE(homeAlias.CanonicalName, sourceNames.HomeTeamName),
                COALESCE(awayAlias.CanonicalName, sourceNames.AwayTeamName)
            )
        ) canonical(LeagueName, HomeTeamName, AwayTeamName)
        WHERE s.League <> canonical.LeagueName
           OR ISNULL(s.StandardizedLeague, N'') <> canonical.LeagueName
           OR s.HomeTeam <> canonical.HomeTeamName
           OR ISNULL(s.StandardizedHomeTeam, N'') <> canonical.HomeTeamName
           OR s.AwayTeam <> canonical.AwayTeamName
           OR ISNULL(s.StandardizedAwayTeam, N'') <> canonical.AwayTeamName;
    END;

    IF OBJECT_ID(N'dbo.TeamMapping', N'U') IS NOT NULL
    BEGIN
        UPDATE tm
        SET StandardizedTeam = alias.CanonicalName
        FROM dbo.TeamMapping tm
        INNER JOIN dbo.TeamNameAlias alias
            ON alias.AliasKey = dbo.fn_NormalizeNameKey(tm.StandardizedTeam)
        WHERE tm.StandardizedTeam <> alias.CanonicalName;
    END;

    IF OBJECT_ID(N'dbo.LeagueMapping', N'U') IS NOT NULL
    BEGIN
        UPDATE lm
        SET StandardizedLeague = alias.CanonicalName
        FROM dbo.LeagueMapping lm
        INNER JOIN dbo.LeagueNameAlias alias
            ON alias.AliasKey = dbo.fn_NormalizeNameKey(lm.StandardizedLeague)
        WHERE lm.StandardizedLeague <> alias.CanonicalName;
    END;

    COMMIT TRANSACTION;
END;
GO
