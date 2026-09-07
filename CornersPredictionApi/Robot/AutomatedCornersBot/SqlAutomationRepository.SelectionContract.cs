using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AutomatedCornersBot.Api;

public sealed partial class SqlAutomationRepository
{
    private async Task EnsureSelectionProcedureContractAsync(
        SqlConnection connection, string schemaPath, CancellationToken cancellationToken)
    {
        // A migration ledger can outlive a restored or manually replaced procedure.
        // Validate the actual callable contract before allowing any bot to publish.
        var source = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        var procedure = SplitSqlBatches(source).Single(batch => Regex.IsMatch(batch,
            @"^CREATE\s+OR\s+ALTER\s+PROCEDURE\s+dbo\.sp_UpsertAutomatedCornerBetSelection\b",
            RegexOptions.IgnoreCase | RegexOptions.Multiline));
        var headerEnd = Regex.Match(procedure, @"^AS\s*$", RegexOptions.Multiline).Index;
        var expected = Regex.Matches(procedure[..headerEnd], @"^\s*(@\w+)\s+", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expected.Count == 0)
            throw new InvalidOperationException("The selection procedure declaration has no parameters.");

        async Task<HashSet<string>> ReadParametersAsync()
        {
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT name FROM sys.parameters WHERE object_id = OBJECT_ID(N'dbo.sp_UpsertAutomatedCornerBetSelection');";
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(0));
            return names;
        }

        var actual = await ReadParametersAsync();
        if (expected.SetEquals(actual)) return;
        _logger.LogWarning("Repairing the selection procedure contract. Missing={Missing}; Unexpected={Unexpected}",
            string.Join(',', expected.Except(actual)), string.Join(',', actual.Except(expected)));
        await using var repair = connection.CreateCommand();
        repair.CommandText = procedure;
        repair.CommandTimeout = 60;
        await repair.ExecuteNonQueryAsync(cancellationToken);
        if (!expected.SetEquals(await ReadParametersAsync()))
            throw new InvalidOperationException("The selection procedure contract could not be restored.");
        _logger.LogInformation("Selection procedure contract restored: {ParameterCount} parameters.", expected.Count);
    }
}
