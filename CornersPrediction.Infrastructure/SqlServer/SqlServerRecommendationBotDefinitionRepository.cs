using System.Data;
using System.Text.RegularExpressions;
using CornersPrediction.Application.Automation;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerRecommendationBotDefinitionRepository : IRecommendationBotDefinitionRepository
{
    private readonly string _connectionString;

    public SqlServerRecommendationBotDefinitionRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetAllAsync(CancellationToken cancellationToken) =>
        QueryAsync(null, cancellationToken);

    public Task<IReadOnlyList<RecommendationBotDefinitionDto>> GetByKeysAsync(
        IReadOnlyCollection<string> botKeys,
        CancellationToken cancellationToken) =>
        QueryAsync(string.Join(',', botKeys), cancellationToken);

    public async Task<RecommendationBotDefinitionDto?> GetAsync(
        string botKey,
        CancellationToken cancellationToken) =>
        (await QueryAsync(botKey, cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<RecommendationBotLeagueCatalogItem>> GetLeagueCatalogAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
WITH LeagueSources AS
(
    SELECT DISTINCT
        NULLIF(LTRIM(RTRIM(COALESCE(NULLIF(StandardizedLeague, N''), League))), N'') AS League,
        N'Cuotas actuales' AS Source,
        CAST(NULL AS nvarchar(100)) AS Country
    FROM dbo.PartidosProximosCuotas
    UNION ALL
    SELECT DISTINCT
        NULLIF(LTRIM(RTRIM(DbLeagueName)), N''),
        N'API-Football',
        NULLIF(LTRIM(RTRIM(Country)), N'')
    FROM dbo.ApiFootballLeagueSeasons
    UNION ALL
    SELECT DISTINCT
        NULLIF(LTRIM(RTRIM(LeagueName)), N''),
        N'API-Football',
        NULLIF(LTRIM(RTRIM(Country)), N'')
    FROM dbo.ApiFootballLeagueSeasons
    UNION ALL
    SELECT DISTINCT
        NULLIF(LTRIM(RTRIM(StandardizedLeague)), N''),
        N'Mapeo de ligas',
        CAST(NULL AS nvarchar(100))
    FROM dbo.LeagueMapping
    UNION ALL
    SELECT DISTINCT
        NULLIF(LTRIM(RTRIM(SourceLeague)), N''),
        N'Mapeo de ligas',
        CAST(NULL AS nvarchar(100))
    FROM dbo.LeagueMapping
), DistinctSources AS
(
    SELECT DISTINCT League, Source, Country
    FROM LeagueSources
    WHERE League IS NOT NULL
)
SELECT
    League,
    Source,
    Country
FROM DistinctSources
ORDER BY League, Source;
""";

        await using var connection = new SqlConnection(_connectionString);
        var rows = (await connection.QueryAsync<LeagueCatalogRow>(new CommandDefinition(
            sql,
            commandTimeout: 30,
            cancellationToken: cancellationToken))).ToArray();

        return rows
            .GroupBy(row => row.League.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new RecommendationBotLeagueCatalogItem(
                ResolveCountry(group.Key, group.Select(row => row.Country)),
                group.Key,
                group.Select(row => row.Source)
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(item => item.Country, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.League, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<RecommendationBotDefinitionDto> UpsertAsync(
        SaveRecommendationBotDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("BotKey", command.BotKey, DbType.String, size: 50);
        parameters.Add("DisplayName", command.DisplayName, DbType.String, size: 120);
        parameters.Add("Description", command.Description, DbType.String, size: 1000);
        parameters.Add("BaseStrategy", command.BaseStrategy, DbType.String, size: 30);
        parameters.Add("IsEnabled", command.IsEnabled, DbType.Boolean);
        parameters.Add("PublishEnabled", command.PublishEnabled ?? true, DbType.Boolean);
        parameters.Add("MarketFamilies", string.Join(',', command.MarketFamilies!), DbType.String, size: 200);
        parameters.Add("MinEdge", command.MinEdge, DbType.Double);
        parameters.Add("MinExpectedValue", command.MinExpectedValue, DbType.Double);
        parameters.Add("MinDistanceToLine", command.MinDistanceToLine, DbType.Double);
        parameters.Add("MaxContextDifference", command.MaxContextDifference, DbType.Double);
        parameters.Add("AllowModelDisagreement", command.AllowModelDisagreement, DbType.Boolean);
        parameters.Add("MinOddsExclusive", command.MinOddsExclusive, DbType.Double);
        parameters.Add("MinProbabilityLiftOverImplied", command.MinProbabilityLiftOverImplied, DbType.Double);
        parameters.Add("StakeMultiplier", command.StakeMultiplier, DbType.Decimal, precision: 9, scale: 4);
        parameters.Add("StrategyConfigurationJson", command.StrategyConfigurationJson, DbType.String);
        parameters.Add(
            "LeagueFilterJson",
            RecommendationBotLeaguePolicy.ToJson(command.LeagueFilters),
            DbType.String);

        await using var connection = new SqlConnection(_connectionString);
        var row = await connection.QuerySingleAsync<BotDefinitionRow>(new CommandDefinition(
            "dbo.sp_UpsertAutomatedBotDefinition",
            parameters,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return ToDto(row);
    }

    public async Task<bool> DeleteAsync(string botKey, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "dbo.sp_DeleteAutomatedBotDefinition",
            new { BotKey = botKey },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken)) > 0;
    }

    private async Task<IReadOnlyList<RecommendationBotDefinitionDto>> QueryAsync(
        string? botKeys,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<BotDefinitionRow>(new CommandDefinition(
            "dbo.sp_GetAutomatedBotDefinitions",
            new { BotKeys = botKeys },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return rows.Select(ToDto).ToArray();
    }

    private static RecommendationBotDefinitionDto ToDto(BotDefinitionRow row) =>
        new(
            row.BotKey,
            row.DisplayName,
            row.Description,
            row.BaseStrategy,
            row.IsEnabled,
            row.PublishEnabled,
            row.IsBuiltIn,
            row.MarketFamilies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            row.MinEdge,
            row.MinExpectedValue,
            row.MinDistanceToLine,
            row.MaxContextDifference,
            row.AllowModelDisagreement,
            row.MinOddsExclusive,
            row.MinProbabilityLiftOverImplied,
            row.StakeMultiplier,
            row.StrategyConfigurationJson,
            row.CreatedAtUtc,
            row.UpdatedAtUtc)
        {
            LeagueFilters = RecommendationBotLeaguePolicy.FromJson(row.LeagueFilterJson)
        };

    private sealed class BotDefinitionRow
    {
        public string BotKey { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string BaseStrategy { get; init; } = string.Empty;
        public bool IsEnabled { get; init; }
        public bool PublishEnabled { get; init; } = true;
        public bool IsBuiltIn { get; init; }
        public string MarketFamilies { get; init; } = string.Empty;
        public double? MinEdge { get; init; }
        public double? MinExpectedValue { get; init; }
        public double? MinDistanceToLine { get; init; }
        public double? MaxContextDifference { get; init; }
        public bool? AllowModelDisagreement { get; init; }
        public double? MinOddsExclusive { get; init; }
        public double? MinProbabilityLiftOverImplied { get; init; }
        public decimal? StakeMultiplier { get; init; }
        public string? StrategyConfigurationJson { get; init; }
        public string? LeagueFilterJson { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
    }

    private static string ResolveCountry(string league, IEnumerable<string?> mappedCountries)
    {
        var mapped = mappedCountries.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(mapped))
        {
            return mapped.Trim();
        }

        if (league.Contains("Chile", StringComparison.OrdinalIgnoreCase)
            || league.Contains("Chilen", StringComparison.OrdinalIgnoreCase))
        {
            return "Chile";
        }

        var prefix = Regex.Match(league, @"^([^()]{2,40}?)\s+-\s+");
        if (prefix.Success)
        {
            var candidate = prefix.Groups[1].Value.Trim();
            if (candidate is "UEFA" or "CONMEBOL" or "CONCACAF" or "AFC" or "CAF")
            {
                return "Internacional";
            }

            return candidate;
        }

        var suffix = Regex.Match(league, @"\(([^()]{2,40})\)\s*$");
        return suffix.Success ? suffix.Groups[1].Value.Trim() : "Otros";
    }

    private sealed class LeagueCatalogRow
    {
        public string League { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string? Country { get; init; }
    }
}
