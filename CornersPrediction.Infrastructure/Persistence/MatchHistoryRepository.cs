using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.MatchHistory;
using Microsoft.EntityFrameworkCore;

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
        CancellationToken cancellationToken)
    {
        var homeMatches = await _dbContext.MatchHistoryItems
            .AsNoTracking()
            .Where(item => item.HomeTeam == homeTeam)
            .OrderByDescending(item => item.MatchDate)
            .ThenByDescending(item => item.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        var awayMatches = await _dbContext.MatchHistoryItems
            .AsNoTracking()
            .Where(item => item.AwayTeam == awayTeam)
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
}
