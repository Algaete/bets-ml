using CornersPrediction.Application.Abstractions.Persistence;

namespace CornersPrediction.Application.Teams;

/// <summary>
/// Loads team metadata used by the web form dropdowns.
/// </summary>
public sealed class GetTeamBi3InfoUseCase : IGetTeamBi3InfoUseCase
{
    private readonly ITeamInfoRepository _repository;
    private readonly Dictionary<string, IReadOnlyList<TeamBi3InfoDto>> _requestCache =
        new(StringComparer.OrdinalIgnoreCase);

    public GetTeamBi3InfoUseCase(ITeamInfoRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<TeamBi3InfoDto>> GetAsync(
        string league,
        string? teamGender,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            return Array.Empty<TeamBi3InfoDto>();
        }

        var normalizedLeague = league.Trim();
        var normalizedGender = TeamGenderOptions.Normalize(teamGender);
        var cacheKey = $"{normalizedGender}|{normalizedLeague}";
        if (_requestCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var teams = await _repository.GetBi3InfoAsync(
            normalizedLeague,
            normalizedGender,
            cancellationToken);

        var result = teams
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
        _requestCache[cacheKey] = result;
        return result;
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

    public async Task<IReadOnlyList<string>> GetAsync(string? teamGender, CancellationToken cancellationToken)
    {
        var leagues = await _repository.GetBig3LeaguesAsync(
            TeamGenderOptions.Normalize(teamGender),
            cancellationToken);

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
