using System.Reflection;
using AutomatedCornersBot.Api;

internal static class SqlBatchSplitterTests
{
    public static void Run()
    {
        var method = typeof(SqlAutomationRepository).GetMethod("SplitSqlBatches", BindingFlags.NonPublic | BindingFlags.Static)!;
        string[] Split(string sql) => ((IReadOnlyList<string>)method.Invoke(null, [sql])!).ToArray();
        var declaration = "CREATE OR ALTER PROCEDURE dbo.Example AS SELECT 1;";
        foreach (var ending in new[] { "\nGO\n", "\r\nGO\r\n", "\nGO -- final batch\n" })
        {
            var batches = Split(declaration + ending);
            if (batches.Length != 1 || batches[0] != declaration)
                throw new InvalidOperationException("A single procedure with a trailing GO must not send GO to SQL Server.");
        }
        if (Split("SELECT 1;\nGO\nSELECT 2;\nGO\n").Length != 2)
            throw new InvalidOperationException("Multiple SQL batches must remain separate.");
        if (Split("SELECT 'GO';").Single() != "SELECT 'GO';")
            throw new InvalidOperationException("GO inside a SQL value is not a batch separator.");
        Console.WriteLine("PASS SQL batch splitting: one trailing GO, CRLF, comments, multiple batches and literal GO");
    }
}
