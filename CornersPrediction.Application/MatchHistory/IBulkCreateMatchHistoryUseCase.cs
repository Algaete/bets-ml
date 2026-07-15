namespace CornersPrediction.Application.MatchHistory;

public interface IBulkCreateMatchHistoryUseCase
{
    Task<BulkMatchHistoryImportResultDto> CreateAsync(
        BulkCreateMatchHistoryCommand command,
        CancellationToken cancellationToken);
}

