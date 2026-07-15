namespace CornersPrediction.Web.Models.MatchHistory;

public sealed record BulkMatchHistoryImportResultViewModel(
    int TotalRows,
    int InsertedCount,
    int DuplicateCount,
    int ErrorCount,
    IReadOnlyList<BulkMatchHistoryImportRowViewModel> Rows);

public sealed record BulkMatchHistoryImportRowViewModel(
    int RowNumber,
    DateOnly? MatchDate,
    string? HomeTeam,
    string? AwayTeam,
    string Status,
    string Message,
    long? InsertedId);

