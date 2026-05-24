using CornersPrediction.Application.Abstractions.Persistence;

namespace CornersPrediction.Application.Teams;

/// <summary>
/// Loads team metadata used by the web form dropdowns.
/// </summary>
public sealed class GetTeamBi3InfoUseCase : IGetTeamBi3InfoUseCase
{
    private readonly ITeamInfoRepository _repository;

    public GetTeamBi3InfoUseCase(ITeamInfoRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<TeamBi3InfoDto>> GetAsync(
        string league,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            return Array.Empty<TeamBi3InfoDto>();
        }

        var teams = await _repository.GetBi3InfoAsync(league.Trim(), cancellationToken);

        return teams
            .OrderBy(team => team.League)
            .ThenBy(team => team.Season)
            .ThenBy(team => team.Team)
            .Select(team => new TeamBi3InfoDto(
                team.League,
                team.Season,
                team.Team,
                team.IsBig3,
                team.CreatedAt))
            .ToArray();
    }
}

/// <summary>
/// Loads the available leagues used to filter team dropdown data.
/// </summary>
public sealed class GetTeamBig3LeaguesUseCase : IGetTeamBig3LeaguesUseCase
{
    private readonly ITeamInfoRepository _repository;

    public GetTeamBig3LeaguesUseCase(ITeamInfoRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<string>> GetAsync(CancellationToken cancellationToken)
    {
        var leagues = await _repository.GetBig3LeaguesAsync(cancellationToken);

        return leagues
            .Where(league => !string.IsNullOrWhiteSpace(league))
            .Select(league => league.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(league => league)
            .ToArray();
    }
}

public sealed class GetFormationListUseCase : IGetFormationListUseCase
{
    private readonly ITeamInfoRepository _repository;

    public GetFormationListUseCase(ITeamInfoRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<string>> GetAsync(CancellationToken cancellationToken)
    {
        var formations = await _repository.GetFormationsAsync(cancellationToken);

        return formations
            .Where(formation => !string.IsNullOrWhiteSpace(formation))
            .Select(formation => formation.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(formation => formation)
            .ToArray();
    }
}
