using System.Text.RegularExpressions;
using CornersPrediction.Application.AutomatedCorners;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed partial class SqlServerAutomatedBotResearchRepository
{
    private static string BuildGeneralPicksQuery(AutomatedBotResearchQuery query)
    {
        var column = AutomatedBotGeneralPickSorting.NormalizeColumn(query.SortBy);
        var direction = query.SortDirection == "asc" ? "ASC" : query.SortDirection == "desc" ? "DESC"
            : throw new ArgumentException("Invalid sort direction.");
        var filtered = query.ModelDecision is not null || query.PublicationStatus is not null
            || column == "PublicationStatus";
        var approvedFastPath = query.ModelDecision == "Approved" && query.PublicationStatus is null;
        var auditSource = filtered
            ? "FROM #GeneralAuditKeys AS keys INNER JOIN dbo.AutomatedBotPickEvaluations AS evaluation WITH (INDEX(IX_AutomatedBotPickEvaluations_ResearchPage)) ON evaluation.AutomatedBotPickEvaluationId = keys.EvaluationId"
            : GeneralAuditScopeSql;
        var count = filtered ? "SELECT COUNT_BIG(*) FROM #GeneralAuditKeys"
            : "SELECT COUNT_BIG(*) " + GeneralAuditScopeSql;
        var filteredKeysSql = approvedFastPath ? GeneralApprovedAuditKeysSql : GeneralFilteredAuditKeysSql;
        var sql = "SET NOCOUNT ON;\n" + (filtered ? filteredKeysSql : "")
            + GeneralPublishedKeysSql + $"\nSELECT ({count}) + (SELECT COUNT_BIG(*) FROM #GeneralPublishedKeys) OPTION (RECOMPILE);\n";
        var auditSort = column switch
        {
            "EvaluationId" => "evaluation.AutomatedBotPickEvaluationId",
            "ModelDecision" => "CASE WHEN evaluation.Decision = N'Rejected' AND evaluation.PerformanceLegacyPublicationRejection = 1 THEN N'Approved' ELSE evaluation.Decision END",
            "PublicationStatus" => "keys.PublicationStatus",
            "SelectionScore" => "COALESCE(evaluation.SelectionScore,evaluation.GSelectionScore,evaluation.RuleBasedConfidenceScore)",
            "OutcomeStatus" => "outcome.OutcomeStatus",
            _ => "evaluation." + column
        };
        var publishedSort = column switch
        {
            "EvaluationId" => "-selection.AutomatedCornerBetSelectionId",
            "ModelDecision" => "CONVERT(NVARCHAR(30),N'NotRecorded')",
            "PublicationStatus" => "CONVERT(NVARCHAR(30),N'Published')",
            "FinalProbability" => "selection.ModelProbability",
            "FinalEdge" => "selection.ProbabilityEdge",
            "FinalExpectedValue" => "selection.ExpectedValue",
            "SelectedOdds" => "selection.Odds",
            "OutcomeStatus" => "CASE WHEN selection.Status=N'Won' AND selection.SettlementFactor=0.5 THEN N'HalfWin' WHEN selection.Status=N'Lost' AND selection.SettlementFactor=-0.5 THEN N'HalfLoss' WHEN selection.Status=N'Won' THEN N'Win' WHEN selection.Status=N'Lost' THEN N'Loss' ELSE selection.Status END",
            _ => "selection." + column
        };
        var auditCandidates = $"SELECT evaluation.AutomatedBotPickEvaluationId AS EvaluationId, evaluation.MatchDate, {auditSort} AS SortValue {auditSource}";
        if (filtered && column is "PublicationStatus" or "ModelDecision" or "MatchDate" or "EvaluationId")
            auditCandidates = $"SELECT keys.EvaluationId, keys.MatchDate, keys.{column} AS SortValue FROM #GeneralAuditKeys AS keys";
        if (column == "OutcomeStatus")
        {
            // Outcomes must be resolved before global sorting. Reuse the exact
            // official/manual resolver, but keep audit JSON out of that scan.
            sql += $"SELECT evaluation.AutomatedBotPickEvaluationId AS EvaluationId, evaluation.MatchDate INTO #GeneralSortResearchPageKeys {auditSource} OPTION (RECOMPILE);\n";
            sql += BuildOutcomeSortProjection().Replace("#Research", "#GeneralSortResearch", StringComparison.Ordinal);
            sql += "DROP TABLE #GeneralSortResearchPageKeys, #GeneralSortResearchPageRows, #GeneralSortResearchFallbackScopes, #GeneralSortResearchFallbackHistory, #GeneralSortResearchFallbackIdentities;\n";
            auditCandidates = "SELECT EvaluationId, MatchDate, OutcomeStatus AS SortValue FROM #GeneralOutcomeSort";
        }
        var order = $"CASE WHEN candidate.SortValue IS NULL THEN 1 ELSE 0 END, candidate.SortValue {direction}, candidate.MatchDate DESC, candidate.EvaluationId DESC";
        sql += $"""
            SELECT candidate.EvaluationId, candidate.MatchDate,
                PageOrdinal = ROW_NUMBER() OVER (ORDER BY {order})
            INTO #ResearchPageKeys
            FROM
            (
                {auditCandidates}
                UNION ALL
                SELECT keys.EvaluationId, keys.MatchDate, {publishedSort}
                FROM #GeneralPublishedKeys AS keys
                INNER JOIN dbo.AutomatedCornerBetSelections AS selection
                  ON selection.AutomatedCornerBetSelectionId = -keys.EvaluationId
            ) AS candidate
            ORDER BY {order}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            OPTION (RECOMPILE);

            """;
        // The full feature snapshot can contain an entire match history. Fetch
        // it separately when the user opens evidence, never on every sort/page.
        var details = ResearchPageDetailsSql.Replace("evaluation.FeatureSnapshotJson,",
            "FeatureSnapshotJson = CONVERT(NVARCHAR(MAX), N''),", StringComparison.Ordinal);
        return sql + details + GeneralPublishedDetailsSql + GeneralBotCatalogSql
            + "\nSELECT EvaluationId FROM #ResearchPageKeys ORDER BY PageOrdinal;";
    }

    private static string BuildOutcomeSortProjection()
    {
        var finalSelect = Regex.Match(ResearchPageDetailsSql, @"SELECT\s+EvaluationId = evaluation\.AutomatedBotPickEvaluationId,");
        var from = ResearchPageDetailsSql.IndexOf("FROM #ResearchPageRows AS evaluation", finalSelect.Index, StringComparison.Ordinal);
        var outcomeStart = ResearchPageDetailsSql.IndexOf("OutcomeStatus = ", finalSelect.Index, StringComparison.Ordinal);
        var outcomeEnd = ResearchPageDetailsSql.IndexOf("OutcomeSource = ", outcomeStart, StringComparison.Ordinal);
        var fallbackStart = ResearchPageDetailsSql.IndexOf("-- Resolve repeated missing-id", StringComparison.Ordinal);
        var firstStage = """
            SELECT evaluation.ApiFootballFixtureId, evaluation.AutomatedBotPickEvaluationId,
                evaluation.BotKey, evaluation.MarketType, evaluation.PublishedSelectionId,
                evaluation.HomeTeam, evaluation.AwayTeam, evaluation.MatchDate,
                evaluation.PredictionTimestampUtc, evaluation.EvaluatedAtUtc,
                evaluation.LineValue, evaluation.SelectedSide, evaluation.SelectedOdds
            INTO #ResearchPageRows
            FROM #ResearchPageKeys AS page
            INNER JOIN dbo.AutomatedBotPickEvaluations AS evaluation
                WITH (INDEX(IX_AutomatedBotPickEvaluations_ResearchPage))
              ON evaluation.AutomatedBotPickEvaluationId = page.EvaluationId
            OPTION (RECOMPILE);

            """ + ResearchPageDetailsSql[fallbackStart..finalSelect.Index];
        var officialJoins = ResearchPageDetailsSql.IndexOf("LEFT JOIN #ResearchFallbackIdentities", from, StringComparison.Ordinal);
        var joins = "FROM #ResearchPageRows AS evaluation\n" + GeneralManualOutcomeApplySql
            + ResearchPageDetailsSql[officialJoins..];
        joins = joins[..joins.LastIndexOf("ORDER BY evaluation.MatchDate DESC", StringComparison.Ordinal)];

        return firstStage + "SELECT EvaluationId = evaluation.AutomatedBotPickEvaluationId, evaluation.MatchDate, "
            + ResearchPageDetailsSql[outcomeStart..outcomeEnd].TrimEnd().TrimEnd(',')
            + " INTO #GeneralOutcomeSort\n" + joins + " OPTION (RECOMPILE);\n";
    }
}
