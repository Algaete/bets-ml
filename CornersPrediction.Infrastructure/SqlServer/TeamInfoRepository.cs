using System.Data;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.Teams;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class TeamInfoRepository : ITeamInfoRepository
{
    private const string LeagueProcedureName = "sp_GetMatchHistoryLeagues";
    private const string TeamProcedureName = "sp_GetTeamsByLeague";
    private const string FormationProcedureName = "sp_GetFormationList";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, StringListCacheEntry> LeagueCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LeagueCacheLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, TeamInfoCacheEntry> TeamInfoCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TeamInfoCacheLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, IReadOnlySet<string>> SupportedParametersCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _connectionString;

    public TeamInfoRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public async Task<IReadOnlyList<string>> GetBig3LeaguesAsync(string teamGender, CancellationToken cancellationToken)
    {
        var cacheKey = teamGender.Trim();
        if (TryGetCachedLeagueList(cacheKey, out var cached))
        {
            return cached;
        }

        var cacheLock = LeagueCacheLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedLeagueList(cacheKey, out cached))
            {
                return cached;
            }

            await using var connection = new SqlConnection(_connectionString);
            var parameters = await BuildSupportedParametersAsync(
                connection,
                LeagueProcedureName,
                new Dictionary<string, object?> { ["TeamGender"] = teamGender },
                cancellationToken);
            var command = new CommandDefinition(
                LeagueProcedureName,
                parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 120,
                cancellationToken: cancellationToken);

            var leagues = (await connection.QueryAsync<string>(command)).ToArray();
            LeagueCache[cacheKey] = new StringListCacheEntry(leagues, DateTime.UtcNow.Add(CacheDuration));
            return leagues;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<TeamBi3Info>> GetBi3InfoAsync(
        string league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{teamGender}|{league.Trim()}";
        if (TeamInfoCache.TryGetValue(cacheKey, out var cached) &&
            cached.ExpiresAtUtc > DateTime.UtcNow)
        {
            return cached.Teams;
        }

        var cacheLock = TeamInfoCacheLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (TeamInfoCache.TryGetValue(cacheKey, out cached) &&
                cached.ExpiresAtUtc > DateTime.UtcNow)
            {
                return cached.Teams;
            }

            await using var connection = new SqlConnection(_connectionString);
            var parameters = await BuildSupportedParametersAsync(
                connection,
                TeamProcedureName,
                new Dictionary<string, object?>
                {
                    ["League"] = league,
                    ["TeamGender"] = teamGender
                },
                cancellationToken);
            var command = new CommandDefinition(
                TeamProcedureName,
                parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 120,
                cancellationToken: cancellationToken);

            var teams = await connection.QueryAsync<TeamInfoRow>(command);
            var result = teams
                .Select(team => new TeamBi3Info
                {
                    League = string.IsNullOrWhiteSpace(team.League) ? league : team.League,
                    Season = team.Season ?? string.Empty,
                    Team = string.IsNullOrWhiteSpace(team.Team) ? team.StandardizedTeam ?? string.Empty : team.Team,
                    IsBig3 = team.IsBig3,
                    CreatedAt = team.CreatedAt
                })
                .Where(team => !string.IsNullOrWhiteSpace(team.Team))
                .ToArray();
            TeamInfoCache[cacheKey] = new TeamInfoCacheEntry(
                result,
                DateTime.UtcNow.Add(CacheDuration));
            return result;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetFormationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        var command = new CommandDefinition(
            FormationProcedureName,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var formations = await connection.QueryAsync<string?>(command);
        return formations
            .Where(formation => !string.IsNullOrWhiteSpace(formation))
            .Select(formation => formation!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(formation => formation)
            .ToArray();
    }

    private static async Task<DynamicParameters> BuildSupportedParametersAsync(
        SqlConnection connection,
        string procedureName,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        if (!SupportedParametersCache.TryGetValue(procedureName, out var supported))
        {
            var command = new CommandDefinition(
                """
                SELECT ParameterName = REPLACE(p.name, '@', '')
                FROM sys.parameters p
                WHERE p.object_id = OBJECT_ID(@ProcedureName);
                """,
                new { ProcedureName = procedureName },
                cancellationToken: cancellationToken);

            supported = (await connection.QueryAsync<string>(command))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            SupportedParametersCache[procedureName] = supported;
        }

        var parameters = new DynamicParameters();
        foreach (var value in values)
        {
            if (supported.Contains(value.Key))
            {
                parameters.Add(value.Key, value.Value);
            }
        }

        return parameters;
    }

    private sealed class TeamInfoRow
    {
        public string? League { get; init; }
        public string? Season { get; init; }
        public string? Team { get; init; }
        public string? StandardizedTeam { get; init; }
        public bool IsBig3 { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed record TeamInfoCacheEntry(
        IReadOnlyList<TeamBi3Info> Teams,
        DateTime ExpiresAtUtc);

    private static bool TryGetCachedLeagueList(string cacheKey, out IReadOnlyList<string> leagues)
    {
        if (LeagueCache.TryGetValue(cacheKey, out var cached) &&
            cached.ExpiresAtUtc > DateTime.UtcNow)
        {
            leagues = cached.Values;
            return true;
        }

        leagues = Array.Empty<string>();
        return false;
    }

    private sealed record StringListCacheEntry(
        IReadOnlyList<string> Values,
        DateTime ExpiresAtUtc);
}
