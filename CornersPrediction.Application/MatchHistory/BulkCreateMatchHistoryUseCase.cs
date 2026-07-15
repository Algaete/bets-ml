using System.Text.Json;
using CornersPrediction.Application.Abstractions.Persistence;
using CornersPrediction.Domain.MatchHistory;

namespace CornersPrediction.Application.MatchHistory;

public sealed class BulkCreateMatchHistoryUseCase : IBulkCreateMatchHistoryUseCase
{
    private const int MaxBulkRows = 500;

    private readonly IMatchHistoryRepository _repository;

    public BulkCreateMatchHistoryUseCase(IMatchHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<BulkMatchHistoryImportResultDto> CreateAsync(
        BulkCreateMatchHistoryCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var request = new MatchHistoryBulkImportRequest(
            command.League.Trim(),
            command.Season.Trim(),
            command.FocusTeam.Trim(),
            NormalizeTeamGender(command.TeamGender),
            command.IsKnockout,
            command.MatchesJson.Trim());

        var result = await _repository.BulkImportAsync(request, cancellationToken);

        return new BulkMatchHistoryImportResultDto(
            result.TotalRows,
            result.InsertedCount,
            result.DuplicateCount,
            result.ErrorCount,
            result.Rows
                .Select(row => new BulkMatchHistoryImportRowDto(
                    row.RowNumber,
                    row.MatchDate,
                    row.HomeTeam,
                    row.AwayTeam,
                    row.Status,
                    row.Message,
                    row.InsertedId))
                .ToArray());
    }

    private static void Validate(BulkCreateMatchHistoryCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.League))
        {
            throw new ArgumentException("League is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Season))
        {
            throw new ArgumentException("Season is required.");
        }

        if (string.IsNullOrWhiteSpace(command.FocusTeam))
        {
            throw new ArgumentException("A team from the database is required.");
        }

        if (string.IsNullOrWhiteSpace(command.MatchesJson))
        {
            throw new ArgumentException("Matches JSON is required.");
        }

        try
        {
            using var document = JsonDocument.Parse(command.MatchesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Matches JSON must be an array.");
            }

            var rowCount = document.RootElement.GetArrayLength();
            if (rowCount == 0)
            {
                throw new ArgumentException("Matches JSON must include at least one match.");
            }

            if (rowCount > MaxBulkRows)
            {
                throw new ArgumentException($"Bulk import supports up to {MaxBulkRows} matches per request.");
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"Matches JSON is invalid: {exception.Message}", exception);
        }
    }

    private static string NormalizeTeamGender(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "M" : value.Trim().ToUpperInvariant();
        return normalized is "M" or "F" or "U" ? normalized : "M";
    }
}

