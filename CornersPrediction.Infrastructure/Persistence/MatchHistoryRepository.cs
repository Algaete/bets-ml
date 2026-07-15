using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.MatchHistory;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CornersPrediction.Infrastructure.Persistence;

public sealed class MatchHistoryRepository : IMatchHistoryRepository
{
    private readonly CornersPredictionDbContext _dbContext;

    public MatchHistoryRepository(CornersPredictionDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<MatchHistoryItem> AddAsync(MatchHistoryItem item, CancellationToken cancellationToken)
    {
        _dbContext.MatchHistoryItems.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return item;
    }

    public async Task<MatchHistoryBulkImportResult> BulkImportAsync(
        MatchHistoryBulkImportRequest request,
        CancellationToken cancellationToken)
    {
        var payloads = JsonSerializer.Deserialize<IReadOnlyList<BulkImportMatchPayload>>(
            request.MatchesJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<BulkImportMatchPayload>();
        var results = new List<MatchHistoryBulkImportRow>(payloads.Count);

        for (var index = 0; index < payloads.Count; index++)
        {
            var rowNumber = index + 1;
            var payload = payloads[index];

            try
            {
                ValidateBulkPayload(request, payload);

                var isDuplicate = await _dbContext.MatchHistoryItems
                    .AsNoTracking()
                    .AnyAsync(item =>
                        item.League == request.League &&
                        item.Season == request.Season &&
                        item.MatchDate == payload.MatchDate &&
                        item.HomeTeam == payload.HomeTeam &&
                        item.AwayTeam == payload.AwayTeam,
                        cancellationToken);

                if (isDuplicate)
                {
                    results.Add(new MatchHistoryBulkImportRow(
                        rowNumber,
                        payload.MatchDate,
                        payload.HomeTeam,
                        payload.AwayTeam,
                        "Duplicate",
                        "This match already exists.",
                        null));
                    continue;
                }

                var item = new MatchHistoryItem
                {
                    League = request.League,
                    Season = request.Season,
                    MatchDate = payload.MatchDate!.Value,
                    IsKnockout = request.IsKnockout,
                    HomeTeam = payload.HomeTeam!.Trim(),
                    AwayTeam = payload.AwayTeam!.Trim(),
                    HomeFormation = NormalizeOptional(payload.HomeFormation),
                    AwayFormation = NormalizeOptional(payload.AwayFormation),
                    HomeGoals = payload.HomeGoals!.Value,
                    AwayGoals = payload.AwayGoals!.Value,
                    HomeCorners = payload.HomeCorners!.Value,
                    AwayCorners = payload.AwayCorners!.Value,
                    HomeShots = payload.HomeShots!.Value,
                    AwayShots = payload.AwayShots!.Value,
                    HomeShotsOnGoal = payload.HomeShotsOnGoal!.Value,
                    AwayShotsOnGoal = payload.AwayShotsOnGoal!.Value,
                    HomePossession = payload.HomePossession!.Value,
                    AwayPossession = payload.AwayPossession!.Value
                };

                _dbContext.MatchHistoryItems.Add(item);
                await _dbContext.SaveChangesAsync(cancellationToken);

                results.Add(new MatchHistoryBulkImportRow(
                    rowNumber,
                    payload.MatchDate,
                    payload.HomeTeam,
                    payload.AwayTeam,
                    "Inserted",
                    "Match inserted.",
                    item.Id));
            }
            catch (Exception exception)
            {
                results.Add(new MatchHistoryBulkImportRow(
                    rowNumber,
                    payload.MatchDate,
                    payload.HomeTeam,
                    payload.AwayTeam,
                    "Error",
                    exception.Message,
                    null));
            }
        }

        return new MatchHistoryBulkImportResult(
            results.Count,
            results.Count(row => row.Status.Equals("Inserted", StringComparison.OrdinalIgnoreCase)),
            results.Count(row => row.Status.Equals("Duplicate", StringComparison.OrdinalIgnoreCase)),
            results.Count(row => row.Status.Equals("Error", StringComparison.OrdinalIgnoreCase)),
            results);
    }

    public async Task<int> UpdateAsync(
        int id,
        MatchHistoryItem item,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.MatchHistoryItems
            .FirstOrDefaultAsync(match => match.Id == id, cancellationToken);

        if (existing is null)
        {
            return 0;
        }

        existing.League = item.League;
        existing.Season = item.Season;
        existing.MatchDate = item.MatchDate;
        existing.IsKnockout = item.IsKnockout;
        existing.HomeTeam = item.HomeTeam;
        existing.AwayTeam = item.AwayTeam;
        existing.HomeFormation = item.HomeFormation;
        existing.AwayFormation = item.AwayFormation;
        existing.HomeGoals = item.HomeGoals;
        existing.AwayGoals = item.AwayGoals;
        existing.HomeCorners = item.HomeCorners;
        existing.AwayCorners = item.AwayCorners;
        existing.HomeShots = item.HomeShots;
        existing.AwayShots = item.AwayShots;
        existing.HomeShotsOnGoal = item.HomeShotsOnGoal;
        existing.AwayShotsOnGoal = item.AwayShotsOnGoal;
        existing.HomePossession = item.HomePossession;
        existing.AwayPossession = item.AwayPossession;

        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.MatchHistoryItems
            .FirstOrDefaultAsync(match => match.Id == id, cancellationToken);

        if (existing is null)
        {
            return 0;
        }

        _dbContext.MatchHistoryItems.Remove(existing);
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetRecentAsync(
        string homeTeam,
        string awayTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var homeMatches = await _dbContext.MatchHistoryItems
            .AsNoTracking()
            .Where(item => item.HomeTeam == homeTeam)
            .Where(item => string.IsNullOrWhiteSpace(league) || item.League == league)
            .OrderByDescending(item => item.MatchDate)
            .ThenByDescending(item => item.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        var awayMatches = await _dbContext.MatchHistoryItems
            .AsNoTracking()
            .Where(item => item.AwayTeam == awayTeam)
            .Where(item => string.IsNullOrWhiteSpace(league) || item.League == league)
            .OrderByDescending(item => item.MatchDate)
            .ThenByDescending(item => item.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var item in homeMatches)
        {
            item.TeamCondition = "HOME";
        }

        foreach (var item in awayMatches)
        {
            item.TeamCondition = "AWAY";
        }

        return homeMatches.Concat(awayMatches).ToArray();
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetManualEntriesAsync(
        string? league,
        string? team,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.MatchHistoryItems.AsNoTracking();

        query = FilterByLeague(query, league);

        if (!string.IsNullOrWhiteSpace(team))
        {
            query = query.Where(item => item.HomeTeam.Contains(team) || item.AwayTeam.Contains(team));
        }

        return await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.MatchDate)
            .ThenByDescending(item => item.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetLast10GeneralMatchesAsync(
        string team,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.MatchHistoryItems
            .AsNoTracking()
            .Where(item => item.HomeTeam == team || item.AwayTeam == team);

        query = FilterByLeague(query, league);

        var matches = await query
            .OrderByDescending(item => item.MatchDate)
            .ThenByDescending(item => item.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var item in matches)
        {
            item.TeamCondition = item.HomeTeam == team ? "HOME" : "AWAY";
        }

        return matches;
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetLast10HomeMatchesAsync(
        string homeTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.MatchHistoryItems
            .AsNoTracking()
            .Where(item => item.HomeTeam == homeTeam);

        query = FilterByLeague(query, league);

        var matches = await query
            .OrderByDescending(item => item.MatchDate)
            .ThenByDescending(item => item.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var item in matches)
        {
            item.TeamCondition = "HOME";
        }

        return matches;
    }

    public async Task<IReadOnlyList<MatchHistoryItem>> GetLast10AwayMatchesAsync(
        string awayTeam,
        string? league,
        string teamGender,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.MatchHistoryItems
            .AsNoTracking()
            .Where(item => item.AwayTeam == awayTeam);

        query = FilterByLeague(query, league);

        var matches = await query
            .OrderByDescending(item => item.MatchDate)
            .ThenByDescending(item => item.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var item in matches)
        {
            item.TeamCondition = "AWAY";
        }

        return matches;
    }

    private static IQueryable<MatchHistoryItem> FilterByLeague(
        IQueryable<MatchHistoryItem> query,
        string? league)
    {
        return string.IsNullOrWhiteSpace(league)
            ? query
            : query.Where(item => item.League == league);
    }

    private static void ValidateBulkPayload(
        MatchHistoryBulkImportRequest request,
        BulkImportMatchPayload payload)
    {
        if (payload.MatchDate is null)
        {
            throw new ArgumentException("Match date is required.");
        }

        if (string.IsNullOrWhiteSpace(payload.HomeTeam) || string.IsNullOrWhiteSpace(payload.AwayTeam))
        {
            throw new ArgumentException("Home team and away team are required.");
        }

        if (!payload.HomeTeam.Equals(request.FocusTeam, StringComparison.OrdinalIgnoreCase) &&
            !payload.AwayTeam.Equals(request.FocusTeam, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The selected team is not present in this match.");
        }

        if (payload.HomeTeam.Equals(payload.AwayTeam, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Home team and away team must be different.");
        }

        if (payload.HomeGoals is null ||
            payload.AwayGoals is null ||
            payload.HomeCorners is null ||
            payload.AwayCorners is null ||
            payload.HomeShots is null ||
            payload.AwayShots is null ||
            payload.HomeShotsOnGoal is null ||
            payload.AwayShotsOnGoal is null ||
            payload.HomePossession is null ||
            payload.AwayPossession is null)
        {
            throw new ArgumentException("All numeric stats are required.");
        }

        if (payload.HomePossession is < 0 or > 100 ||
            payload.AwayPossession is < 0 or > 100)
        {
            throw new ArgumentException("Possession values must be between 0 and 100.");
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class BulkImportMatchPayload
    {
        public DateOnly? MatchDate { get; init; }
        public string? HomeTeam { get; init; }
        public string? AwayTeam { get; init; }
        public string? HomeFormation { get; init; }
        public string? AwayFormation { get; init; }
        public int? HomeGoals { get; init; }
        public int? AwayGoals { get; init; }
        public int? HomeCorners { get; init; }
        public int? AwayCorners { get; init; }
        public int? HomeShots { get; init; }
        public int? AwayShots { get; init; }
        public int? HomeShotsOnGoal { get; init; }
        public int? AwayShotsOnGoal { get; init; }
        public double? HomePossession { get; init; }
        public double? AwayPossession { get; init; }
    }
}
