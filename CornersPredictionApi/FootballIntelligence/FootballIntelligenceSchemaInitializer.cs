using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CornersPredictionApi.FootballIntelligence;

public sealed class FootballIntelligenceSchemaInitializer
{
    private static readonly Regex GoLine = new(
        @"^\s*GO\s*(?:--.*)?$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile bool _ready;
    private readonly string _connectionString;
    private readonly IWebHostEnvironment _environment;

    public FootballIntelligenceSchemaInitializer(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        _environment = environment;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_ready)
            return;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_ready)
                return;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var scripts = new[]
            {
                "001_CreateNewsTables.sql",
                "004_AddFactIdempotency.sql",
                "002_CreateIndexes.sql",
                "003_CreateSourceConfig.sql"
            };
            // Every script is idempotent. Running all of them also repairs a partially
            // applied deployment instead of treating the presence of two tables as complete.
            foreach (var scriptName in scripts)
            {
                var path = Path.Combine(
                    _environment.ContentRootPath,
                    "SqlScripts",
                    "FootballIntelligence",
                    scriptName);
                if (!File.Exists(path))
                    throw new FileNotFoundException("Football Intelligence SQL script was not found.", path);
                var script = await File.ReadAllTextAsync(path, cancellationToken);
                foreach (var batch in GoLine.Split(script).Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        batch,
                        commandType: CommandType.Text,
                        commandTimeout: 300,
                        cancellationToken: cancellationToken));
                }
            }

            _ready = true;
        }
        finally
        {
            Gate.Release();
        }
    }
}
