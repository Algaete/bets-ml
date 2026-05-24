namespace CornersPrediction.Application.Predictions;

public enum PredictionErrorType
{
    PythonNotFound,
    MissingDependency,
    ScriptNotFound,
    Timeout,
    ProcessFailed,
    InvalidOutput
}

public sealed class PredictionException : Exception
{
    public PredictionException(PredictionErrorType errorType, string message)
        : base(message)
    {
        ErrorType = errorType;
    }

    public PredictionException(PredictionErrorType errorType, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorType = errorType;
    }

    public PredictionErrorType ErrorType { get; }
}
