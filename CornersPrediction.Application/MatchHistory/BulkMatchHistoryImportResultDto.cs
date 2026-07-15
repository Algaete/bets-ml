namespace CornersPrediction.Application.MatchHistory;

public sealed record BulkMatchHistoryImportResultDto(
    int TotalRows,
    int InsertedCount,
    int DuplicateCount,
    int ErrorCount,
    IReadOnlyList<BulkMatchHistoryImportRowDto> Rows);

public sealed record BulkMatchHistoryImportRowDto(
    int RowNumber,
    DateOnly? MatchDate,
    string? HomeTeam,
    string? AwayTeam,
    string Status,
    string Message,
    long? InsertedId);

