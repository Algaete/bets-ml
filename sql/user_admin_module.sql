IF OBJECT_ID('dbo.PlatformRoles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlatformRoles
    (
        Name NVARCHAR(50) NOT NULL CONSTRAINT PK_PlatformRoles PRIMARY KEY,
        Description NVARCHAR(200) NOT NULL
    );
END
GO

MERGE dbo.PlatformRoles AS target
USING
(
    VALUES
        ('Admin', 'Can administer users and platform settings.'),
        ('User', 'Default platform user.'),
        ('Bettor', 'Can use betting and bankroll features.'),
        ('Analyst', 'Can use prediction and analysis features.')
) AS source(Name, Description)
ON target.Name = source.Name
WHEN MATCHED THEN
    UPDATE SET Description = source.Description
WHEN NOT MATCHED THEN
    INSERT (Name, Description) VALUES (source.Name, source.Description);
GO

IF OBJECT_ID('dbo.PlatformUsers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlatformUsers
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlatformUsers PRIMARY KEY,
        ExternalUserId NVARCHAR(450) NULL,
        Email NVARCHAR(320) NOT NULL,
        DisplayName NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_PlatformUsers_IsActive DEFAULT 1,
        CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_PlatformUsers_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2(0) NULL,
        IsDeleted BIT NOT NULL CONSTRAINT DF_PlatformUsers_IsDeleted DEFAULT 0
    );

    CREATE UNIQUE INDEX UX_PlatformUsers_Email_Active
        ON dbo.PlatformUsers(Email)
        WHERE IsDeleted = 0;

    CREATE UNIQUE INDEX UX_PlatformUsers_ExternalUserId_Active
        ON dbo.PlatformUsers(ExternalUserId)
        WHERE IsDeleted = 0 AND ExternalUserId IS NOT NULL;
END
GO

IF OBJECT_ID('dbo.PlatformUserRoles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlatformUserRoles
    (
        PlatformUserId BIGINT NOT NULL,
        RoleName NVARCHAR(50) NOT NULL,
        CreatedAt DATETIME2(0) NOT NULL CONSTRAINT DF_PlatformUserRoles_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_PlatformUserRoles PRIMARY KEY (PlatformUserId, RoleName),
        CONSTRAINT FK_PlatformUserRoles_PlatformUsers FOREIGN KEY (PlatformUserId) REFERENCES dbo.PlatformUsers(Id),
        CONSTRAINT FK_PlatformUserRoles_PlatformRoles FOREIGN KEY (RoleName) REFERENCES dbo.PlatformRoles(Name)
    );
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_InsertPlatformUser
    @ExternalUserId NVARCHAR(450) = NULL,
    @Email NVARCHAR(320),
    @DisplayName NVARCHAR(200),
    @IsActive BIT = 1,
    @RolesCsv NVARCHAR(MAX) = NULL,
    @InsertedId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT dbo.PlatformUsers
    (
        ExternalUserId,
        Email,
        DisplayName,
        IsActive,
        CreatedAt,
        IsDeleted
    )
    VALUES
    (
        NULLIF(LTRIM(RTRIM(@ExternalUserId)), ''),
        LOWER(LTRIM(RTRIM(@Email))),
        LTRIM(RTRIM(@DisplayName)),
        @IsActive,
        SYSUTCDATETIME(),
        0
    );

    SET @InsertedId = CONVERT(BIGINT, SCOPE_IDENTITY());

    INSERT dbo.PlatformUserRoles (PlatformUserId, RoleName)
    SELECT DISTINCT @InsertedId, r.Name
    FROM STRING_SPLIT(COALESCE(NULLIF(@RolesCsv, ''), 'User'), ',') s
    INNER JOIN dbo.PlatformRoles r
        ON r.Name = LTRIM(RTRIM(s.value));
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdatePlatformUser
    @Id BIGINT,
    @ExternalUserId NVARCHAR(450) = NULL,
    @Email NVARCHAR(320),
    @DisplayName NVARCHAR(200),
    @IsActive BIT = 1,
    @RolesCsv NVARCHAR(MAX) = NULL,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.PlatformUsers
    SET
        ExternalUserId = NULLIF(LTRIM(RTRIM(@ExternalUserId)), ''),
        Email = LOWER(LTRIM(RTRIM(@Email))),
        DisplayName = LTRIM(RTRIM(@DisplayName)),
        IsActive = @IsActive,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id
      AND IsDeleted = 0;

    SET @RowsAffected = @@ROWCOUNT;

    IF @RowsAffected > 0
    BEGIN
        DELETE FROM dbo.PlatformUserRoles
        WHERE PlatformUserId = @Id;

        INSERT dbo.PlatformUserRoles (PlatformUserId, RoleName)
        SELECT DISTINCT @Id, r.Name
        FROM STRING_SPLIT(COALESCE(NULLIF(@RolesCsv, ''), 'User'), ',') s
        INNER JOIN dbo.PlatformRoles r
            ON r.Name = LTRIM(RTRIM(s.value));
    END
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_DeletePlatformUser
    @Id BIGINT,
    @RowsAffected INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.PlatformUsers
    SET IsDeleted = 1,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id
      AND IsDeleted = 0;

    SET @RowsAffected = @@ROWCOUNT;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetPlatformUserById
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.Id,
        u.ExternalUserId,
        u.Email,
        u.DisplayName,
        u.IsActive,
        RolesCsv = STRING_AGG(ur.RoleName, ',') WITHIN GROUP (ORDER BY ur.RoleName),
        u.CreatedAt,
        u.UpdatedAt
    FROM dbo.PlatformUsers u
    LEFT JOIN dbo.PlatformUserRoles ur
        ON ur.PlatformUserId = u.Id
    WHERE u.Id = @Id
      AND u.IsDeleted = 0
    GROUP BY u.Id, u.ExternalUserId, u.Email, u.DisplayName, u.IsActive, u.CreatedAt, u.UpdatedAt;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetPlatformUsers
    @Search NVARCHAR(320) = NULL,
    @Role NVARCHAR(50) = NULL,
    @IsActive BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.Id,
        u.ExternalUserId,
        u.Email,
        u.DisplayName,
        u.IsActive,
        RolesCsv = STRING_AGG(ur.RoleName, ',') WITHIN GROUP (ORDER BY ur.RoleName),
        u.CreatedAt,
        u.UpdatedAt
    FROM dbo.PlatformUsers u
    LEFT JOIN dbo.PlatformUserRoles ur
        ON ur.PlatformUserId = u.Id
    WHERE u.IsDeleted = 0
      AND (@IsActive IS NULL OR u.IsActive = @IsActive)
      AND (
            @Search IS NULL
            OR u.Email LIKE '%' + @Search + '%'
            OR u.DisplayName LIKE '%' + @Search + '%'
            OR u.ExternalUserId LIKE '%' + @Search + '%'
          )
      AND (
            @Role IS NULL
            OR EXISTS
            (
                SELECT 1
                FROM dbo.PlatformUserRoles roleFilter
                WHERE roleFilter.PlatformUserId = u.Id
                  AND roleFilter.RoleName = @Role
            )
          )
    GROUP BY u.Id, u.ExternalUserId, u.Email, u.DisplayName, u.IsActive, u.CreatedAt, u.UpdatedAt
    ORDER BY u.DisplayName, u.Email;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetPlatformRoles
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Name, Description
    FROM dbo.PlatformRoles
    ORDER BY Name;
END
GO
