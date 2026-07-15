namespace CornersPrediction.Domain.MatchHistory;

public sealed record MatchHistoryBulkImportResult(
    int TotalRows,
    int InsertedCount,
    int DuplicateCount,
    int ErrorCount,
    IReadOnlyList<MatchHistoryBulkImportRow> Rows);

public sealed record MatchHistoryBulkImportRow(
    int RowNumber,
    DateOnly? MatchDate,
    string? HomeTeam,
    string? AwayTeam,
    string Status,
    string Message,
    long? InsertedId);

