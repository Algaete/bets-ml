namespace CornersPrediction.Application.Automation.BotC;

public sealed record BotCMetaModelInput(
    string FeatureSchemaVersion,
    string MarketType,
    string Selection,
    IReadOnlyDictionary<string, double> NumericFeatures);

public sealed record BotCMetaModelPrediction(
    bool IsAvailable,
    double Probability,
    string ModelName,
    string ModelVersion,
    string? UnavailableReason = null,
    DateTime? TrainedThroughUtc = null)
{
    public static BotCMetaModelPrediction Unavailable(string reason) =>
        new(false, 0, string.Empty, string.Empty, reason);
}

public interface IBotCMetaModelPredictor
{
    BotCMetaModelPrediction Predict(BotCMetaModelInput input);
}

public sealed class UnavailableBotCMetaModelPredictor : IBotCMetaModelPredictor
{
    public BotCMetaModelPrediction Predict(BotCMetaModelInput input) =>
        BotCMetaModelPrediction.Unavailable("No hay un artefacto de meta-modelo activo.");
}
