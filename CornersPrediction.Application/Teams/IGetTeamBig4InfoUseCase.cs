namespace CornersPrediction.Application.Teams;

public interface IGetTeamBi3InfoUseCase
{
    Task<IReadOnlyList<TeamBi3InfoDto>> GetAsync(string league, string? teamGender, CancellationToken cancellationToken);
}

public interface IGetTeamBig3LeaguesUseCase
{
    Task<IReadOnlyList<string>> GetAsync(string? teamGender, CancellationToken cancellationToken);
}

public interface IGetFormationListUseCase
{
    Task<IReadOnlyList<string>> GetAsync(CancellationToken cancellationToken);
}
