using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CornersMLData.Data
{
    /// <summary>
    /// Creates the database alias catalog, seeds it from CanonicalNameCatalog and
    /// rewrites old operational rows to their canonical identity.
    /// </summary>
    public sealed class CanonicalNameNormalizationRepository
    {
        // Aliases are seeded on every startup. Only bump this version when a deliberate
        // full-table rewrite through sp_ApplyCanonicalNames is required.
        private const string CatalogVersion = "2026-07-19-v7";
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private bool _initialized;

        public CanonicalNameNormalizationRepository(
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
                return;

            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                if (_initialized)
                    return;

                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

                var scriptPath = Path.Combine(_environment.ContentRootPath, "SqlScripts", "Create_CanonicalNames.sql");
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException("No se encontro el script SQL de nombres canonicos.", scriptPath);

                var script = await File.ReadAllTextAsync(scriptPath, cancellationToken);
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                foreach (var batch in SplitSqlBatches(script))
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        batch,
                        commandTimeout: 180,
                        cancellationToken: cancellationToken));
                }

                await SeedAliasesAsync(connection, "dbo.TeamNameAlias", CanonicalNameCatalog.GetTeamAliases(), cancellationToken);
                await SeedAliasesAsync(connection, "dbo.LeagueNameAlias", CanonicalNameCatalog.GetLeagueAliases(), cancellationToken);

                var appliedVersion = await connection.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
                    "SELECT CatalogVersion FROM dbo.CanonicalNameNormalizationState WHERE StateId = 1;",
                    commandTimeout: 60,
                    cancellationToken: cancellationToken));

                if (!CatalogVersion.Equals(appliedVersion, StringComparison.Ordinal))
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        "dbo.sp_ApplyCanonicalNames",
                        commandType: System.Data.CommandType.StoredProcedure,
                        commandTimeout: 180,
                        cancellationToken: cancellationToken));

                    await connection.ExecuteAsync(new CommandDefinition(
                        """
MERGE dbo.CanonicalNameNormalizationState WITH (HOLDLOCK) AS Target
USING (SELECT CAST(1 AS TINYINT) AS StateId, @CatalogVersion AS CatalogVersion) AS Source
    ON Target.StateId = Source.StateId
WHEN MATCHED THEN
    UPDATE SET CatalogVersion = Source.CatalogVersion, AppliedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (StateId, CatalogVersion, AppliedAtUtc)
    VALUES (Source.StateId, Source.CatalogVersion, SYSUTCDATETIME());
""",
                        new { CatalogVersion },
                        commandTimeout: 60,
                        cancellationToken: cancellationToken));
                }

                _initialized = true;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private static async Task SeedAliasesAsync(
            SqlConnection connection,
            string tableName,
            IReadOnlyCollection<CanonicalNameAlias> aliases,
            CancellationToken cancellationToken)
        {
            const string sqlTemplate = """
MERGE {0} WITH (HOLDLOCK) AS Target
USING
(
    SELECT AliasKey, CanonicalName
    FROM OPENJSON(@AliasesJson)
    WITH
    (
        AliasKey NVARCHAR(250) '$.AliasKey',
        CanonicalName NVARCHAR(200) '$.CanonicalName'
    )
) AS Source
    ON Target.AliasKey = Source.AliasKey
WHEN MATCHED THEN
    UPDATE SET
        Target.CanonicalName = Source.CanonicalName,
        Target.UpdatedAtUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (AliasKey, CanonicalName, UpdatedAtUtc)
    VALUES (Source.AliasKey, Source.CanonicalName, SYSUTCDATETIME());
""";

            var sql = string.Format(sqlTemplate, tableName);
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { AliasesJson = JsonSerializer.Serialize(aliases) },
                commandTimeout: 60,
                cancellationToken: cancellationToken));
        }

        private static IReadOnlyCollection<string> SplitSqlBatches(string script) =>
            Regex.Split(script, @"(?im)^\s*GO\s*(?:--.*)?$")
                .Select(batch => batch.Trim())
                .Where(batch => !string.IsNullOrWhiteSpace(batch))
                .ToArray();
    }
}
