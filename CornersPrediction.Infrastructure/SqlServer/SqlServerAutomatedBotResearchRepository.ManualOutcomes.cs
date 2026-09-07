namespace CornersPrediction.Infrastructure.SqlServer;

public sealed partial class SqlServerAutomatedBotResearchRepository
{
    // Manual outcomes live outside the immutable model audit and official feed.
    // Later feed refreshes therefore cannot overwrite an operator's settlement.
    private const string GeneralManualOutcomeApplySql = """

        OUTER APPLY
        (
            SELECT TOP (1) result.OutcomeStatus, result.ActualValue, result.ProfitLoss,
                result.Reason, result.SettledBy, result.SettledAtUtc
            FROM
            (
                SELECT entered.OutcomeStatus, entered.ActualValue, entered.ProfitLoss,
                    entered.Reason, entered.SettledBy, entered.SettledAtUtc, Priority = 1, entered.Id
                FROM dbo.GeneralBotPickManualSettlements AS entered
                WHERE entered.RecordId = evaluation.AutomatedBotPickEvaluationId
                   OR entered.RecordId = -evaluation.PublishedSelectionId
                UNION ALL
                SELECT CASE
                    WHEN selection.Status = N'Won' AND selection.SettlementFactor = 0.5 THEN N'HalfWin'
                    WHEN selection.Status = N'Lost' AND selection.SettlementFactor = -0.5 THEN N'HalfLoss'
                    WHEN selection.Status = N'Won' THEN N'Win'
                    WHEN selection.Status = N'Lost' THEN N'Loss' ELSE selection.Status END,
                    selection.SettlementActualValue,
                    CONVERT(DECIMAL(12,4), CASE WHEN selection.Stake > 0 THEN selection.ProfitLoss / selection.Stake END),
                    selection.SettlementReason, N'Administrador (registro previo)', selection.SettledAtUtc, 0, 0
                FROM dbo.AutomatedCornerBetSelections AS selection
                WHERE selection.AutomatedCornerBetSelectionId = evaluation.PublishedSelectionId
                  AND selection.SettlementSource = N'Manual' AND selection.Status <> N'Pending'
            ) AS result
            ORDER BY result.SettledAtUtc DESC, result.Priority DESC, result.Id DESC
        ) AS manual

        """;
}
