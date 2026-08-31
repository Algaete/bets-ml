SET NOCOUNT ON;

MERGE dbo.FootballSourceConfiguration AS target
USING
(
    VALUES
        (N'fifa.com', N'Official', CONVERT(DECIMAL(9,6), 1.00)),
        (N'uefa.com', N'Official', CONVERT(DECIMAL(9,6), 1.00)),
        (N'api-football.com', N'StructuredProvider', CONVERT(DECIMAL(9,6), 0.95)),
        (N'espn.com', N'MajorMedia', CONVERT(DECIMAL(9,6), 0.85)),
        (N'bbc.com', N'MajorMedia', CONVERT(DECIMAL(9,6), 0.85)),
        (N'marca.com', N'MajorMedia', CONVERT(DECIMAL(9,6), 0.85))
) AS source(Domain, SourceTier, ConfidenceWeight)
ON target.Domain = source.Domain
WHEN NOT MATCHED THEN
    INSERT (Domain, SourceTier, ConfidenceWeight, IsEnabled)
    VALUES (source.Domain, source.SourceTier, source.ConfidenceWeight, 1);
