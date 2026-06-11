using System.Net;
using System.Text;
using CornersPrediction.Web.Models.Betting;

namespace CornersPrediction.Web.Services;

public static class BettingExportBuilder
{
    public static byte[] BuildExcel(
        BettingIndexViewModel model,
        DateTime generatedAtUtc)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<html><head><meta charset=\"utf-8\" /></head><body>");
        builder.AppendLine("<h1>Betting export</h1>");
        builder.AppendLine($"<p>Generated at {generatedAtUtc:yyyy-MM-dd HH:mm} UTC</p>");

        builder.AppendLine("<h2>Summary</h2>");
        builder.AppendLine("<table border=\"1\">");
        AppendRow(builder, "Currency", model.WorkingCurrencyCode);
        AppendRow(builder, "Current bankroll", model.CurrentBankroll);
        AppendRow(builder, "Total bets", model.Summary.TotalBets);
        AppendRow(builder, "Pending bets", model.Summary.PendingBets);
        AppendRow(builder, "Won bets", model.Summary.WonBets);
        AppendRow(builder, "Lost bets", model.Summary.LostBets);
        AppendRow(builder, "Total stake", model.Summary.TotalStake);
        AppendRow(builder, "Net P/L", model.Summary.TotalProfitLoss);
        AppendRow(builder, "ROI %", model.Summary.RoiPercent);
        AppendRow(builder, "Win rate %", model.Summary.WinRatePercent);
        AppendRow(builder, "Average odds", model.Summary.AverageOdds);
        builder.AppendLine("</table>");

        builder.AppendLine("<h2>Betting records</h2>");
        builder.AppendLine("<table border=\"1\">");
        AppendHeader(builder,
            "Id", "Date", "League", "Season", "Match", "Bookmaker", "Market", "Selection",
            "Prediction model", "Line", "Odds", "Stake", "Status", "Actual corners", "Actual shots", "Actual SOG", "Potential return",
            "Net return", "Profit/Loss", "ROI %", "Bankroll before", "Bankroll after",
            "Confidence", "Notes");

        foreach (var record in model.Records)
        {
            AppendRow(builder,
                record.Id,
                record.MatchDate.ToString("yyyy-MM-dd"),
                record.League,
                record.Season,
                $"{record.HomeTeam} vs {record.AwayTeam}",
                record.Bookmaker,
                record.MarketType,
                record.BetSelection,
                record.PredictionModel,
                record.Line,
                record.Odds,
                record.Stake,
                record.Status,
                record.ActualTotalCorners,
                record.ActualTotalShots,
                record.ActualTotalShotsOnGoal,
                record.PotentialReturn,
                record.NetReturn,
                record.ProfitLoss,
                record.RoiPercent,
                record.BankrollBefore,
                record.BankrollAfter,
                record.ConfidenceLevel,
                record.Notes);
        }

        builder.AppendLine("</table>");

        builder.AppendLine("<h2>Bankroll movements</h2>");
        builder.AppendLine("<table border=\"1\">");
        AppendHeader(builder, "Id", "Date", "Currency", "Type", "Amount", "Balance after", "Bet id", "Notes");
        foreach (var transaction in model.BankrollTransactions)
        {
            AppendRow(builder,
                transaction.Id,
                transaction.TransactionDate.ToString("yyyy-MM-dd"),
                transaction.CurrencyCode,
                transaction.Type,
                transaction.Amount,
                transaction.BalanceAfter,
                transaction.BettingRecordId,
                transaction.Notes);
        }

        builder.AppendLine("</table>");
        builder.AppendLine("</body></html>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static byte[] BuildPdf(
        BettingIndexViewModel model,
        DateTime generatedAtUtc)
    {
        var lines = new List<string>
        {
            "Betting export",
            $"Generated at {generatedAtUtc:yyyy-MM-dd HH:mm} UTC",
            $"Currency: {model.WorkingCurrencyCode}",
            $"Current bankroll: {FormatMoney(model.CurrentBankroll, model.WorkingCurrencyCode)}",
            $"Total bets: {model.Summary.TotalBets}",
            $"Total stake: {FormatMoney(model.Summary.TotalStake, model.WorkingCurrencyCode)}",
            $"Net P/L: {FormatMoney(model.Summary.TotalProfitLoss, model.WorkingCurrencyCode)}",
            $"ROI: {model.Summary.RoiPercent:N2}%",
            $"Win rate: {model.Summary.WinRatePercent:N2}%",
            string.Empty,
            "Betting records"
        };

        foreach (var record in model.Records)
        {
            lines.Add($"{record.MatchDate:yyyy-MM-dd} | {record.HomeTeam} vs {record.AwayTeam}");
            lines.Add($"  {record.MarketType} {record.BetSelection} {record.Line:N2} @ {record.Odds:N2} | Stake {FormatMoney(record.Stake, record.CurrencyCode)} | {record.Status} | {FormatActualMarketResult(record)} | P/L {FormatMoney(record.ProfitLoss, record.CurrencyCode)}");
        }

        lines.Add(string.Empty);
        lines.Add("Bankroll movements");
        foreach (var transaction in model.BankrollTransactions)
        {
            lines.Add($"{transaction.TransactionDate:yyyy-MM-dd} | {transaction.Type} | {FormatMoney(transaction.Amount, transaction.CurrencyCode)} | Balance {FormatMoney(transaction.BalanceAfter, transaction.CurrencyCode)} | Bet {(transaction.BettingRecordId?.ToString() ?? "-")}");
        }

        return SimplePdf.Build(lines);
    }

    private static void AppendHeader(StringBuilder builder, params string[] values)
    {
        builder.AppendLine("<tr>");
        foreach (var value in values)
        {
            builder.Append("<th>").Append(WebUtility.HtmlEncode(value)).AppendLine("</th>");
        }

        builder.AppendLine("</tr>");
    }

    private static void AppendRow(StringBuilder builder, params object?[] values)
    {
        builder.AppendLine("<tr>");
        foreach (var value in values)
        {
            builder.Append("<td>").Append(WebUtility.HtmlEncode(FormatCell(value))).AppendLine("</td>");
        }

        builder.AppendLine("</tr>");
    }

    private static string FormatCell(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd"),
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd"),
            decimal number => number.ToString("0.##"),
            double number => number.ToString("0.##"),
            float number => number.ToString("0.##"),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string FormatActualMarketResult(BettingRecordViewModel record)
    {
        return record.MarketType switch
        {
            "TotalShots" => $"Shots {(record.ActualTotalShots?.ToString() ?? "-")}",
            "TotalShotsOnGoal" => $"SOG {(record.ActualTotalShotsOnGoal?.ToString() ?? "-")}",
            _ => $"Corners {(record.ActualTotalCorners?.ToString() ?? "-")}"
        };
    }

    private static string FormatMoney(decimal value, string currencyCode)
    {
        return currencyCode == "CLP"
            ? $"{currencyCode} {value:N0}"
            : $"{currencyCode} {value:N2}";
    }

    private static class SimplePdf
    {
        private const int LinesPerPage = 44;

        public static byte[] Build(IReadOnlyList<string> sourceLines)
        {
            var lines = sourceLines
                .SelectMany(SplitLongLine)
                .ToArray();
            var pageCount = Math.Max(1, (int)Math.Ceiling(lines.Length / (double)LinesPerPage));
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pageCount).Select(index => $"{3 + index * 2} 0 R"))}] /Count {pageCount} >>"
            };

            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var pageObjectNumber = 3 + pageIndex * 2;
                var contentObjectNumber = pageObjectNumber + 1;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Contents {contentObjectNumber} 0 R >>");
                var content = BuildPageContent(lines.Skip(pageIndex * LinesPerPage).Take(LinesPerPage));
                objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
            }

            return BuildPdfBytes(objects);
        }

        private static string BuildPageContent(IEnumerable<string> lines)
        {
            var builder = new StringBuilder();
            builder.AppendLine("BT");
            builder.AppendLine("/F1 9 Tf");
            builder.AppendLine("40 760 Td");

            var isFirstLine = true;
            foreach (var line in lines)
            {
                if (!isFirstLine)
                {
                    builder.AppendLine("0 -15 Td");
                }

                builder.Append('(').Append(EscapePdf(line)).AppendLine(") Tj");
                isFirstLine = false;
            }

            builder.Append("ET");
            return builder.ToString();
        }

        private static byte[] BuildPdfBytes(IReadOnlyList<string> objects)
        {
            using var stream = new MemoryStream();
            WriteAscii(stream, "%PDF-1.4\n");

            var offsets = new List<long> { 0 };
            for (var index = 0; index < objects.Count; index++)
            {
                offsets.Add(stream.Position);
                WriteAscii(stream, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            }

            var xrefPosition = stream.Position;
            WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
            WriteAscii(stream, "0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1))
            {
                WriteAscii(stream, $"{offset:0000000000} 00000 n \n");
            }

            WriteAscii(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF");
            return stream.ToArray();
        }

        private static IEnumerable<string> SplitLongLine(string line)
        {
            const int maxLength = 105;
            if (line.Length <= maxLength)
            {
                yield return line;
                yield break;
            }

            for (var index = 0; index < line.Length; index += maxLength)
            {
                yield return line.Substring(index, Math.Min(maxLength, line.Length - index));
            }
        }

        private static string EscapePdf(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);
        }

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
