USE [res]
GO

IF OBJECT_ID(N'dbo.PartidosProximos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartidosProximos
    (
        PartidoID INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_PartidosProximos PRIMARY KEY,
        FechaPartido DATETIME2(0) NOT NULL,
        EquipoLocal VARCHAR(150) NOT NULL,
        EquipoVisita VARCHAR(150) NOT NULL,
        Liga VARCHAR(150) NOT NULL,
        Genero VARCHAR(50) NOT NULL,
        EsKnockout BIT NOT NULL
            CONSTRAINT DF_PartidosProximos_EsKnockout DEFAULT (0),
        TotalTeams INT NULL,
        HomeTeamPosition INT NULL,
        AwayTeamPosition INT NULL,
        FechaRegistro DATETIME2(3) NOT NULL
            CONSTRAINT DF_PartidosProximos_FechaRegistro DEFAULT (SYSUTCDATETIME()),
        FechaActualizacion DATETIME2(3) NOT NULL
            CONSTRAINT DF_PartidosProximos_FechaActualizacion DEFAULT (SYSUTCDATETIME())
    );
END
GO

IF COL_LENGTH(N'dbo.PartidosProximos', N'TotalTeams') IS NULL
BEGIN
    ALTER TABLE dbo.PartidosProximos
    ADD TotalTeams INT NULL;
END
GO

IF COL_LENGTH(N'dbo.PartidosProximos', N'HomeTeamPosition') IS NULL
BEGIN
    ALTER TABLE dbo.PartidosProximos
    ADD HomeTeamPosition INT NULL;
END
GO

IF COL_LENGTH(N'dbo.PartidosProximos', N'AwayTeamPosition') IS NULL
BEGIN
    ALTER TABLE dbo.PartidosProximos
    ADD AwayTeamPosition INT NULL;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE [type] = 'UQ'
      AND [name] = N'UQ_PartidosProximos_Fecha_Equipos_Liga'
      AND [parent_object_id] = OBJECT_ID(N'dbo.PartidosProximos')
)
BEGIN
    ALTER TABLE dbo.PartidosProximos
    ADD CONSTRAINT UQ_PartidosProximos_Fecha_Equipos_Liga
        UNIQUE (FechaPartido, EquipoLocal, EquipoVisita, Liga);
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpsertPartidoProximo
    @FechaPartido DATETIME2(0),
    @EquipoLocal VARCHAR(150),
    @EquipoVisita VARCHAR(150),
    @Liga VARCHAR(150),
    @Genero VARCHAR(50),
    @EsKnockout BIT,
    @TotalTeams INT = NULL,
    @HomeTeamPosition INT = NULL,
    @AwayTeamPosition INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @FechaPartido IS NULL
        THROW 50001, 'FechaPartido es obligatorio.', 1;

    IF NULLIF(LTRIM(RTRIM(@EquipoLocal)), '') IS NULL
        THROW 50002, 'EquipoLocal es obligatorio.', 1;

    IF NULLIF(LTRIM(RTRIM(@EquipoVisita)), '') IS NULL
        THROW 50003, 'EquipoVisita es obligatorio.', 1;

    IF NULLIF(LTRIM(RTRIM(@Liga)), '') IS NULL
        THROW 50004, 'Liga es obligatorio.', 1;

    IF NULLIF(LTRIM(RTRIM(@Genero)), '') IS NULL
        THROW 50005, 'Genero es obligatorio.', 1;

    MERGE dbo.PartidosProximos WITH (HOLDLOCK) AS Target
    USING
    (
        SELECT
            @FechaPartido AS FechaPartido,
            LTRIM(RTRIM(@EquipoLocal)) AS EquipoLocal,
            LTRIM(RTRIM(@EquipoVisita)) AS EquipoVisita,
            LTRIM(RTRIM(@Liga)) AS Liga,
            LTRIM(RTRIM(@Genero)) AS Genero,
            @EsKnockout AS EsKnockout,
            @TotalTeams AS TotalTeams,
            @HomeTeamPosition AS HomeTeamPosition,
            @AwayTeamPosition AS AwayTeamPosition
    ) AS Source
    ON  Target.FechaPartido = Source.FechaPartido
    AND Target.EquipoLocal = Source.EquipoLocal
    AND Target.EquipoVisita = Source.EquipoVisita
    AND Target.Liga = Source.Liga
    WHEN MATCHED THEN
        UPDATE SET
            Target.Genero = Source.Genero,
            Target.EsKnockout = Source.EsKnockout,
            Target.TotalTeams = Source.TotalTeams,
            Target.HomeTeamPosition = Source.HomeTeamPosition,
            Target.AwayTeamPosition = Source.AwayTeamPosition,
            Target.FechaActualizacion = SYSUTCDATETIME()
    WHEN NOT MATCHED THEN
        INSERT
        (
            FechaPartido,
            EquipoLocal,
            EquipoVisita,
            Liga,
            Genero,
            EsKnockout,
            TotalTeams,
            HomeTeamPosition,
            AwayTeamPosition,
            FechaRegistro,
            FechaActualizacion
        )
        VALUES
        (
            Source.FechaPartido,
            Source.EquipoLocal,
            Source.EquipoVisita,
            Source.Liga,
            Source.Genero,
            Source.EsKnockout,
            Source.TotalTeams,
            Source.HomeTeamPosition,
            Source.AwayTeamPosition,
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        );
END
GO
