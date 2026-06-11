using CornersPrediction.Domain.Teams;

namespace CornersPrediction.Application.Abstractions.Persistence;

/// <summary>
/// Persistence port for team metadata stored in SQL Server.
/// </summary>
public interface ITeamInfoRepository
{
    Task<IReadOnlyList<string>> GetBig3LeaguesAsync(string teamGender, CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamBi3Info>> GetBi3InfoAsync(string league, string teamGender, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetFormationsAsync(CancellationToken cancellationToken);
}
