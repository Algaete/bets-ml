using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CornersPrediction.Application.RobustPickEvaluation;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CornersPrediction.Infrastructure.SqlServer;

public sealed class SqlServerRobustPickEvaluationRepository : IRobustPickEvaluationRepository
{
    private const string SchemaFileName = "20260829_robust_pick_evaluation.sql";
    private static readonly Regex SqlBatchSeparator = new(
        @"^\s*GO\s*(?:--.*)?$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private readonly string _connectionString;
    private readonly string _schemaPath;
    private bool _schemaReady;

    public SqlServerRobustPickEvaluationRepository(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        _schemaPath = Path.Combine(environment.ContentRootPath, "sql", SchemaFileName);
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady) return;

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady) return;
            if (!File.Exists(_schemaPath))
                throw new FileNotFoundException("The Robust Pick Evaluation SQL migration was not found.", _schemaPath);

            var sql = await File.ReadAllTextAsync(_schemaPath, cancellationToken);
            await using var connection = await OpenConnectionAsync(cancellationToken);
            const string migrationKey = "robust:20260829_robust_pick_evaluation.sql";
            var contentHash = Sha256(sql);
            await EnsureMigrationLedgerAsync(connection, cancellationToken);
            var appliedHash = await GetAppliedMigrationHashAsync(connection, migrationKey, cancellationToken);
            if (string.Equals(appliedHash, contentHash, StringComparison.OrdinalIgnoreCase))
            {
                _schemaReady = true;
                return;
            }
            if (appliedHash is null && await CanBootstrapAppliedSchemaAsync(connection, cancellationToken))
            {
                await RecordAppliedMigrationAsync(connection, migrationKey, contentHash, cancellationToken);
                _schemaReady = true;
                return;
            }
            foreach (var batch in SqlBatchSeparator.Split(sql))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await connection.ExecuteAsync(new CommandDefinition(
                    batch,
                    commandTimeout: 300,
                    cancellationToken: cancellationToken));
            }
            await RecordAppliedMigrationAsync(connection, migrationKey, contentHash, cancellationToken);

            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private static async Task EnsureMigrationLedgerAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF OBJECT_ID(N'dbo.ApplicationSchemaMigrations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ApplicationSchemaMigrations
                (
                    MigrationKey NVARCHAR(200) NOT NULL CONSTRAINT PK_ApplicationSchemaMigrations PRIMARY KEY,
                    ContentHash CHAR(64) NOT NULL,
                    AppliedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_ApplicationSchemaMigrations_AppliedAtUtc DEFAULT SYSUTCDATETIME()
                );
            END;
            """,
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    private static Task<string?> GetAppliedMigrationHashAsync(
        SqlConnection connection,
        string migrationKey,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT ContentHash FROM dbo.ApplicationSchemaMigrations WHERE MigrationKey = @MigrationKey;",
            new { MigrationKey = migrationKey },
            commandTimeout: 30,
            cancellationToken: cancellationToken));

    private static async Task RecordAppliedMigrationAsync(
        SqlConnection connection,
        string migrationKey,
        string contentHash,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ApplicationSchemaMigrations
            SET ContentHash = @ContentHash, AppliedAtUtc = SYSUTCDATETIME()
            WHERE MigrationKey = @MigrationKey;
            IF @@ROWCOUNT = 0
                INSERT dbo.ApplicationSchemaMigrations(MigrationKey, ContentHash)
                VALUES (@MigrationKey, @ContentHash);
            """,
            new { MigrationKey = migrationKey, ContentHash = contentHash },
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    private static async Task<bool> CanBootstrapAppliedSchemaAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CONVERT(BIT, CASE WHEN
                OBJECT_ID(N'dbo.AutomatedBotPickRobustEvaluations', N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.sp_AppendAutomatedBotPickRobustEvaluation', N'P') IS NOT NULL
                AND EXISTS
                (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.AutomatedBotPickEvaluations')
                      AND name = N'IX_AutomatedBotPickEvaluations_RobustResidualHistory'
                )
                THEN 1 ELSE 0 END);
            """,
            commandTimeout: 30,
            cancellationToken: cancellationToken));
    }

    public async Task<AppendRobustEvaluationResult> AppendAsync(
        AppendRobustPickEvaluationCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        await EnsureSchemaAsync(cancellationToken);

        var logicalPickKey = Sha256(BuildLogicalSubject(command));
        var inputPayload = CanonicalizeJson(command.InputPayloadJson, "input payload");
        var evaluationPayload = CanonicalizeJson(command.EvaluationPayloadJson, "evaluation payload");
        var histogram = CanonicalizeJson(command.HistogramJson, "histogram");
        var reasons = CanonicalizeJson(command.RejectionReasonCodesJson, "rejection reasons");
        var warnings = CanonicalizeJson(command.WarningCodesJson, "warnings");

        var snapshotNode = JsonNode.Parse(JsonSerializer.Serialize(command, JsonOptions))!.AsObject();
        snapshotNode.Remove("components");
        snapshotNode["inputPayloadJson"] = inputPayload;
        snapshotNode["evaluationPayloadJson"] = evaluationPayload;
        snapshotNode["histogramJson"] = histogram;
        snapshotNode["rejectionReasonCodesJson"] = reasons;
        snapshotNode["warningCodesJson"] = warnings;
        snapshotNode["asOfUtc"] = EnsureUtc(command.AsOfUtc);
        if (command.ModelTrainedThroughUtc.HasValue)
            snapshotNode["modelTrainedThroughUtc"] = EnsureUtc(command.ModelTrainedThroughUtc.Value);
        if (command.QuoteTimestampUtc.HasValue)
            snapshotNode["quoteTimestampUtc"] = EnsureUtc(command.QuoteTimestampUtc.Value);
        var snapshotJson = CanonicalizeNode(snapshotNode);

        var componentNodes = command.Components
            .OrderBy(component => component.ComponentSequence)
            .ThenBy(component => component.ComponentType, StringComparer.Ordinal)
            .Select(component =>
            {
                var node = JsonNode.Parse(JsonSerializer.Serialize(component, JsonOptions))!.AsObject();
                node["asOfUtc"] = EnsureUtc(component.AsOfUtc);
                node["metadataJson"] = CanonicalizeJson(component.MetadataJson, "component metadata");
                return node;
            })
            .ToArray();
        var componentsJson = CanonicalizeNode(new JsonArray(componentNodes));

        var inputHash = Sha256(string.Join('|',
            logicalPickKey,
            command.EvaluationVersion.Trim(),
            command.RobustnessVersion.Trim(),
            command.PolicyVersion.Trim(),
            EnsureUtc(command.AsOfUtc).ToString("O", CultureInfo.InvariantCulture),
            command.SourceOddsSnapshotId?.ToString(CultureInfo.InvariantCulture) ?? "NO_ODDS_SNAPSHOT",
            inputPayload));
        var snapshotHash = Sha256(snapshotJson + "|" + componentsJson);
        var idempotencyHash = Sha256(string.Join('|',
            logicalPickKey,
            command.EvaluationVersion.Trim(),
            EnsureUtc(command.AsOfUtc).ToString("O", CultureInfo.InvariantCulture),
            inputHash));

        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<AppendRobustEvaluationResult>(new CommandDefinition(
            "dbo.sp_AppendAutomatedBotPickRobustEvaluation",
            new
            {
                LogicalPickKey = logicalPickKey,
                IdempotencyHash = idempotencyHash,
                InputHash = inputHash,
                SnapshotHash = snapshotHash,
                SnapshotJson = snapshotJson,
                ComponentsJson = componentsJson
            },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 120,
            cancellationToken: cancellationToken));
    }

    public async Task<RobustPickEvaluationDetail?> GetCurrentBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken)
    {
        RequirePositive(selectionId, nameof(selectionId));
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(new CommandDefinition(
            """
            DECLARE @CurrentEvaluationId BIGINT =
            (
                SELECT TOP (1) RobustEvaluationId
                FROM dbo.AutomatedBotPickRobustEvaluations
                WHERE BotPickSelectionId = @SelectionId AND IsCurrent = 1
                ORDER BY AsOfUtc DESC, RobustEvaluationId DESC
            );

            SELECT *
            FROM dbo.AutomatedBotPickRobustEvaluations
            WHERE RobustEvaluationId = @CurrentEvaluationId;

            SELECT component.*
            FROM dbo.AutomatedBotPickRobustComponents AS component
            WHERE component.RobustEvaluationId = @CurrentEvaluationId
            ORDER BY component.ComponentSequence;
            """,
            new { SelectionId = selectionId },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        var evaluation = await result.ReadSingleOrDefaultAsync<RobustPickEvaluationSnapshot>();
        if (evaluation is null) return null;
        var components = (await result.ReadAsync<RobustEvaluationComponentSnapshot>()).AsList();
        return new RobustPickEvaluationDetail(evaluation, components);
    }

    public async Task<IReadOnlyList<RobustPickEvaluationSnapshot>> GetHistoryBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken)
    {
        RequirePositive(selectionId, nameof(selectionId));
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<RobustPickEvaluationSnapshot>(new CommandDefinition(
            """
            SELECT *
            FROM dbo.AutomatedBotPickRobustEvaluations
            WHERE BotPickSelectionId = @SelectionId
            ORDER BY EvaluationSequence DESC;
            """,
            new { SelectionId = selectionId },
            commandTimeout: 60,
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<RobustEvaluationComparisonDto?> GetComparisonBySelectionIdAsync(
        long selectionId,
        CancellationToken cancellationToken)
    {
        RequirePositive(selectionId, nameof(selectionId));
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RobustEvaluationComparisonDto>(new CommandDefinition(
            """
            SELECT TOP (1)
                evaluation.BotPickSelectionId,
                evaluation.RobustEvaluationId,
                evaluation.EvaluationSequence,
                evaluation.EvaluationMode,
                CurrentDecision = evaluation.CurrentSystemDecision,
                ShadowDecision = evaluation.RobustDecision,
                evaluation.OriginalStake,
                evaluation.RecommendedStake,
                StakeDifference = evaluation.RecommendedStake - evaluation.OriginalStake,
                evaluation.RobustnessScore,
                evaluation.RejectionReasonCodesJson,
                evaluation.WarningCodesJson,
                evaluation.HumanReadableReason,
                evaluation.AsOfUtc
            FROM dbo.AutomatedBotPickRobustEvaluations AS evaluation
            WHERE evaluation.BotPickSelectionId = @SelectionId
              AND evaluation.IsCurrent = 1
            ORDER BY evaluation.AsOfUtc DESC, evaluation.RobustEvaluationId DESC;
            """,
            new { SelectionId = selectionId },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    public async Task<RobustEvaluationMetricsDto> GetMetricsAsync(
        RobustEvaluationMetricsFilter filter,
        CancellationToken cancellationToken)
    {
        Validate(filter);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var result = await connection.QueryMultipleAsync(new CommandDefinition(
            MetricsSql,
            new
            {
                FromUtc = AsNullableUtc(filter.FromUtc),
                ToUtc = AsNullableUtc(filter.ToUtc),
                BotKey = Normalize(filter.BotKey),
                MarketFamily = Normalize(filter.MarketFamily),
                MarketType = Normalize(filter.MarketType),
                EvaluationVersion = Normalize(filter.EvaluationVersion)
            },
            commandTimeout: 180,
            cancellationToken: cancellationToken));
        var summary = await result.ReadSingleAsync<RobustEvaluationMetricsDto>();
        var reasons = (await result.ReadAsync<RobustReasonMetricDto>()).AsList();
        return CopyWithReasons(summary, reasons);
    }

    public async Task<RobustBackfillPreviewResult> PreviewBackfillAsync(
        RobustBackfillPreviewFilter filter,
        CancellationToken cancellationToken)
    {
        // This query is always read-only. A real backfill uses the same preview
        // first, then the orchestrator requests immutable candidate pages.
        Validate(filter, requireDryRun: false);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var result = await connection.QuerySingleAsync<RobustBackfillPreviewResult>(new CommandDefinition(
            BackfillPreviewSql,
            new
            {
                FromUtc = EnsureUtc(filter.FromUtc),
                ToUtc = EnsureUtc(filter.ToUtc),
                BotKey = Normalize(filter.BotKey),
                MarketFamily = Normalize(filter.MarketFamily),
                MarketType = Normalize(filter.MarketType),
                filter.FixtureId,
                EvaluationVersion = filter.EvaluationVersion.Trim(),
                filter.Force
            },
            commandTimeout: 180,
            cancellationToken: cancellationToken));
        return new RobustBackfillPreviewResult
        {
            DryRun = true,
            SourceCandidates = result.SourceCandidates,
            EligibleCandidates = result.EligibleCandidates,
            AlreadyEvaluated = result.AlreadyEvaluated,
            MissingPredictionTimestamp = result.MissingPredictionTimestamp,
            MissingModelTrainingCutoff = result.MissingModelTrainingCutoff,
            ModelTrainedAfterPrediction = result.ModelTrainedAfterPrediction,
            MissingImmutableOddsSnapshot = result.MissingImmutableOddsSnapshot,
            OddsSnapshotAfterPrediction = result.OddsSnapshotAfterPrediction,
            MissingBilateralOdds = result.MissingBilateralOdds,
            Message = "Eligibility preview completed; this query did not modify snapshots, picks or settlements."
        };
    }

    public async Task<IReadOnlyList<RobustBackfillCandidateDto>> LoadBackfillCandidatesAsync(
        RobustBackfillPreviewFilter filter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        Validate(filter, requireDryRun: false);
        if (batchSize is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be between 1 and 1000.");

        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<RobustBackfillCandidateDto>(new CommandDefinition(
            BackfillCandidatesSql,
            new
            {
                FromUtc = EnsureUtc(filter.FromUtc),
                ToUtc = EnsureUtc(filter.ToUtc),
                BotKey = Normalize(filter.BotKey),
                MarketFamily = Normalize(filter.MarketFamily)?.ToUpperInvariant(),
                MarketType = Normalize(filter.MarketType),
                filter.FixtureId,
                EvaluationVersion = filter.EvaluationVersion.Trim(),
                filter.Force,
                AfterPredictionTimestampUtc = AsNullableUtc(filter.AfterPredictionTimestampUtc),
                filter.AfterSourceEvaluationId,
                BatchSize = batchSize
            },
            commandTimeout: 180,
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<RobustResidualObservation>> LoadResidualHistoryAsync(
        RobustResidualHistoryQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<RobustResidualObservation>(new CommandDefinition(
            ResidualHistorySql,
            new
            {
                EvaluationAsOfUtc = EnsureUtc(query.EvaluationAsOfUtc),
                MarketFamily = query.MarketFamily.Trim().ToUpperInvariant(),
                MarketType = Normalize(query.MarketType),
                Side = Normalize(query.Side),
                League = Normalize(query.League),
                query.OutcomeAvailabilityLagHours,
                query.MaximumRows
            },
            commandTimeout: 180,
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<OpenPortfolioExposureDto>> LoadOpenExposureAsync(
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var normalizedAsOf = EnsureUtc(asOfUtc);
        if (normalizedAsOf > DateTime.UtcNow.AddMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(asOfUtc), "AsOfUtc cannot be in the future.");
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<OpenPortfolioExposureDto>(new CommandDefinition(
            OpenExposureSql,
            new { AsOfUtc = normalizedAsOf },
            commandTimeout: 120,
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<AppendRobustPolicyResult> AppendPolicyAsync(
        AppendRobustPolicyCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        await EnsureSchemaAsync(cancellationToken);
        var configurationJson = CanonicalizeJson(command.ConfigurationJson, "policy configuration");
        var signature = string.Join('|',
            command.PolicyVersion.Trim(), EnsureUtc(command.EffectiveFromUtc).ToString("O", CultureInfo.InvariantCulture),
            command.EvaluationMode.Trim(), Normalize(command.BotKey), Normalize(command.MarketFamily),
            Normalize(command.MarketType), Normalize(command.MarketScope), Normalize(command.Side),
            Invariant(command.MinimumLine), Invariant(command.MaximumLine),
            Invariant(command.MinimumOdds), Invariant(command.MaximumOdds),
            Normalize(command.LeaguePattern), configurationJson);
        var policyHash = Sha256(signature);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<PolicyAppendRow>(new CommandDefinition(
            """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            BEGIN TRY
                DECLARE @PolicyId BIGINT;
                SELECT @PolicyId = RobustPolicyId
                FROM dbo.AutomatedBotRobustPolicies WITH (UPDLOCK, HOLDLOCK)
                WHERE PolicyHash = @PolicyHash;

                IF @PolicyId IS NULL
                BEGIN
                    INSERT dbo.AutomatedBotRobustPolicies
                    (
                        PolicyHash, PolicyVersion, EffectiveFromUtc, EvaluationMode,
                        BotKey, MarketFamily, MarketType, MarketScope, Side,
                        MinimumLine, MaximumLine, MinimumOdds, MaximumOdds,
                        LeaguePattern, ConfigurationJson, CreatedBy
                    )
                    VALUES
                    (
                        @PolicyHash, @PolicyVersion, @EffectiveFromUtc, @EvaluationMode,
                        @BotKey, @MarketFamily, @MarketType, @MarketScope, @Side,
                        @MinimumLine, @MaximumLine, @MinimumOdds, @MaximumOdds,
                        @LeaguePattern, @ConfigurationJson, @CreatedBy
                    );
                    SET @PolicyId = SCOPE_IDENTITY();
                    COMMIT TRANSACTION;
                    SELECT @PolicyId AS RobustPolicyId, CONVERT(BIT, 1) AS Inserted;
                    RETURN;
                END;

                COMMIT TRANSACTION;
                SELECT @PolicyId AS RobustPolicyId, CONVERT(BIT, 0) AS Inserted;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """,
            new
            {
                PolicyHash = policyHash,
                PolicyVersion = command.PolicyVersion.Trim(),
                EffectiveFromUtc = EnsureUtc(command.EffectiveFromUtc),
                EvaluationMode = command.EvaluationMode.Trim(),
                BotKey = Normalize(command.BotKey),
                MarketFamily = Normalize(command.MarketFamily),
                MarketType = Normalize(command.MarketType),
                MarketScope = Normalize(command.MarketScope),
                Side = Normalize(command.Side),
                command.MinimumLine,
                command.MaximumLine,
                command.MinimumOdds,
                command.MaximumOdds,
                LeaguePattern = Normalize(command.LeaguePattern),
                ConfigurationJson = configurationJson,
                CreatedBy = command.CreatedBy.Trim()
            },
            commandTimeout: 60,
            cancellationToken: cancellationToken));
        return new AppendRobustPolicyResult(row.RobustPolicyId, row.Inserted);
    }

    public async Task<RobustPolicySnapshot?> GetEffectivePolicyAsync(
        RobustPolicyQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RobustPolicySnapshot>(new CommandDefinition(
            PolicyQuerySql + " ORDER BY Specificity DESC, EffectiveFromUtc DESC, RobustPolicyId DESC;",
            PolicyParameters(query),
            commandTimeout: 60,
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<RobustPolicySnapshot>> GetPolicyHistoryAsync(
        RobustPolicyQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<RobustPolicySnapshot>(new CommandDefinition(
            PolicyQuerySql.Replace("SELECT TOP (1)", "SELECT")
                + " ORDER BY EffectiveFromUtc DESC, Specificity DESC, RobustPolicyId DESC;",
            PolicyParameters(query),
            commandTimeout: 60,
            cancellationToken: cancellationToken))).AsList();
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static object PolicyParameters(RobustPolicyQuery query) => new
    {
        AsOfUtc = EnsureUtc(query.AsOfUtc),
        BotKey = Normalize(query.BotKey),
        MarketFamily = Normalize(query.MarketFamily),
        MarketType = Normalize(query.MarketType),
        MarketScope = Normalize(query.MarketScope),
        Side = Normalize(query.Side),
        League = Normalize(query.League),
        query.Line,
        query.Odds
    };

    private static string BuildLogicalSubject(AppendRobustPickEvaluationCommand command)
    {
        var subject = Normalize(command.EvaluationSubjectKey);
        if (subject is null)
        {
            subject = command.BotPickSelectionId.HasValue
                ? $"selection:{command.BotPickSelectionId.Value}"
                : command.SourceEvaluationId.HasValue
                    ? $"source-evaluation:{command.SourceEvaluationId.Value}"
                    : $"fixture:{command.FixtureId}|{command.BotKey}|{command.Bookmaker}|{command.MarketType}|{command.Side}|{command.Line.ToString("0.00", CultureInfo.InvariantCulture)}";
        }
        return subject.Trim().ToUpperInvariant();
    }

    private static void Validate(AppendRobustPickEvaluationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Required(command.BotKey, nameof(command.BotKey), 50);
        Required(command.MarketFamily, nameof(command.MarketFamily), 30);
        Required(command.MarketType, nameof(command.MarketType), 50);
        Required(command.Bookmaker, nameof(command.Bookmaker), 50);
        Required(command.EvaluationVersion, nameof(command.EvaluationVersion), 80);
        Required(command.RobustnessVersion, nameof(command.RobustnessVersion), 120);
        Required(command.PolicyVersion, nameof(command.PolicyVersion), 120);
        Required(command.CurrentSystemDecision, nameof(command.CurrentSystemDecision), 30);
        Required(command.HumanReadableReason, nameof(command.HumanReadableReason), 2000);
        if (command.Side is not ("Over" or "Under"))
            throw new ArgumentException("Side must be Over or Under.", nameof(command));
        if (command.EvaluationMode is not ("Shadow" or "Enforce" or "Disabled"))
            throw new ArgumentException("EvaluationMode must be Shadow, Enforce or Disabled.", nameof(command));
        if (command.RobustDecision is not ("Approve" or "Reject" or "ReduceStake" or "ManualReview"))
            throw new ArgumentException("RobustDecision is invalid.", nameof(command));
        if (command.CurrentSystemDecision is not ("Bet" or "NoBet"))
            throw new ArgumentException("CurrentSystemDecision must be Bet or NoBet.", nameof(command));
        if (command.Line < 0 || command.Odds <= 1)
            throw new ArgumentException("Line must be non-negative and decimal odds must be greater than one.", nameof(command));
        if (command.SourceEvaluationId is <= 0 || command.BotPickSelectionId is <= 0
            || command.SourceOddsSnapshotId is <= 0 || command.FixtureId is <= 0)
            throw new ArgumentException("Optional lineage identifiers must be positive.", nameof(command));
        if (command.OriginalStake < 0 || command.RecommendedStake < 0
            || command.RecommendedStake > command.OriginalStake
            || command.StakeMultiplier is < 0 or > 1)
            throw new ArgumentException("The v1 robust layer may only maintain or reduce the original stake.", nameof(command));
        ValidateProbabilityFields(command);
        if (command.ResidualRawObservationCount is < 0 || command.SimulationCount is < 0
            || command.CalibrationExactMarketN is < 0 || command.CalibrationFamilyN is < 0
            || command.CalibrationGlobalN is < 0 || command.ScenarioCount is < 0
            || command.OddsAgeSeconds is < 0)
            throw new ArgumentException("Counts and evidence ages cannot be negative.", nameof(command));
        if (command.DistributionEffectiveN is < 0 || command.CalibrationEffectiveN is < 0
            || command.StandardDeviation is < 0 || command.MedianAbsoluteDeviation is < 0
            || command.ErrorScale is < 0)
            throw new ArgumentException("Distribution and calibration scales cannot be negative.", nameof(command));
        var asOf = EnsureUtc(command.AsOfUtc);
        if (asOf > DateTime.UtcNow.AddMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(command), "AsOfUtc cannot be in the future.");
        if (command.ModelTrainedThroughUtc.HasValue
            && EnsureUtc(command.ModelTrainedThroughUtc.Value) > asOf)
            throw new ArgumentException("ModelTrainedThroughUtc cannot be later than AsOfUtc.", nameof(command));
        if (command.QuoteTimestampUtc.HasValue && EnsureUtc(command.QuoteTimestampUtc.Value) > asOf)
            throw new ArgumentException("QuoteTimestampUtc cannot be later than AsOfUtc.", nameof(command));
        if (command.Components.Select(component => component.ComponentSequence).Distinct().Count() != command.Components.Count)
            throw new ArgumentException("ComponentSequence must be unique within an evaluation.", nameof(command));
        foreach (var component in command.Components)
        {
            if (component.ComponentSequence <= 0 || string.IsNullOrWhiteSpace(component.ComponentType))
                throw new ArgumentException("Every robust component needs a positive sequence and type.", nameof(command));
            if (component.Weight is < 0 or > 1)
                throw new ArgumentException("Component weights must be between zero and one.", nameof(command));
            InUnitInterval(component.ProbabilityForSelection, nameof(component.ProbabilityForSelection));
            InUnitInterval(component.DataQualityScore, nameof(component.DataQualityScore));
            if (EnsureUtc(component.AsOfUtc) > asOf)
                throw new ArgumentException("A component cannot contain evidence newer than the evaluation AsOfUtc.", nameof(command));
            RequireJsonKind(component.MetadataJson, JsonValueKind.Object, "component metadata");
        }
        _ = CanonicalizeJson(command.InputPayloadJson, "input payload");
        _ = CanonicalizeJson(command.EvaluationPayloadJson, "evaluation payload");
        _ = CanonicalizeJson(command.HistogramJson, "histogram");
        _ = CanonicalizeJson(command.RejectionReasonCodesJson, "rejection reasons");
        _ = CanonicalizeJson(command.WarningCodesJson, "warnings");
        RequireJsonKind(command.InputPayloadJson, JsonValueKind.Object, "input payload");
        RequireJsonKind(command.EvaluationPayloadJson, JsonValueKind.Object, "evaluation payload");
        RequireJsonContainer(command.HistogramJson, "histogram");
        RequireJsonKind(command.RejectionReasonCodesJson, JsonValueKind.Array, "rejection reasons");
        RequireJsonKind(command.WarningCodesJson, JsonValueKind.Array, "warnings");
    }

    private static void ValidateProbabilityFields(AppendRobustPickEvaluationCommand command)
    {
        var probabilities = new (decimal? Value, string Name)[]
        {
            (command.MagnitudeAgreementScore, nameof(command.MagnitudeAgreementScore)),
            (command.ProbabilityAgreementScore, nameof(command.ProbabilityAgreementScore)),
            (command.CoherenceScore, nameof(command.CoherenceScore)),
            (command.ScenarioSideStability, nameof(command.ScenarioSideStability)),
            (command.PositiveEvStability, nameof(command.PositiveEvStability)),
            (command.PWin, nameof(command.PWin)),
            (command.PHalfWin, nameof(command.PHalfWin)),
            (command.PPush, nameof(command.PPush)),
            (command.PHalfLoss, nameof(command.PHalfLoss)),
            (command.PLoss, nameof(command.PLoss)),
            (command.RawProbability, nameof(command.RawProbability)),
            (command.CalibratedProbability, nameof(command.CalibratedProbability)),
            (command.ProbabilityLowerBound, nameof(command.ProbabilityLowerBound)),
            (command.ProbabilityUpperBound, nameof(command.ProbabilityUpperBound)),
            (command.ModelFairProbability, nameof(command.ModelFairProbability)),
            (command.RobustModelFairProbability, nameof(command.RobustModelFairProbability)),
            (command.MarketImpliedProbability, nameof(command.MarketImpliedProbability)),
            (command.MarketNoVigProbability, nameof(command.MarketNoVigProbability)),
            (command.ConservativeMarketProbability, nameof(command.ConservativeMarketProbability)),
            (command.CalibrationReliability, nameof(command.CalibrationReliability)),
            (command.CalibrationSpecificityScore, nameof(command.CalibrationSpecificityScore)),
            (command.CalibrationRecencyScore, nameof(command.CalibrationRecencyScore)),
            (command.CalibrationErrorScore, nameof(command.CalibrationErrorScore)),
            (command.OddsReliability, nameof(command.OddsReliability)),
            (command.AdverseScenarioProbability, nameof(command.AdverseScenarioProbability)),
            (command.ScenarioStability, nameof(command.ScenarioStability)),
            (command.RobustnessScore, nameof(command.RobustnessScore))
        };
        foreach (var field in probabilities) InUnitInterval(field.Value, field.Name);

        if (command.ProbabilityLowerBound.HasValue && command.ProbabilityUpperBound.HasValue
            && command.ProbabilityLowerBound > command.ProbabilityUpperBound)
            throw new ArgumentException("ProbabilityLowerBound cannot exceed ProbabilityUpperBound.", nameof(command));

        var settlement = new[] { command.PWin, command.PHalfWin, command.PPush, command.PHalfLoss, command.PLoss };
        if (settlement.All(value => value.HasValue)
            && Math.Abs(settlement.Sum(value => value!.Value) - 1m) > 0.0001m)
            throw new ArgumentException("Settlement probabilities must sum to one.", nameof(command));

        var quantiles = new[]
        {
            command.P01, command.P05, command.P10, command.P25, command.P50,
            command.P75, command.P90, command.P95, command.P99
        }.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (quantiles.Zip(quantiles.Skip(1), (left, right) => left <= right).Any(valid => !valid))
            throw new ArgumentException("Predictive-distribution quantiles must be monotonic.", nameof(command));
    }

    private static void InUnitInterval(decimal? value, string name)
    {
        if (value is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(name, "The value must be between zero and one.");
    }

    private static void Validate(RobustEvaluationMetricsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.FromUtc.HasValue && filter.ToUtc.HasValue
            && EnsureUtc(filter.ToUtc.Value) <= EnsureUtc(filter.FromUtc.Value))
            throw new ArgumentException("ToUtc must be later than FromUtc.", nameof(filter));
    }

    private static void Validate(RobustBackfillPreviewFilter filter, bool requireDryRun)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (requireDryRun && !filter.DryRun)
            throw new InvalidOperationException("This endpoint is intentionally dry-run only; the evaluator orchestrates real append operations.");
        if (EnsureUtc(filter.ToUtc) <= EnsureUtc(filter.FromUtc))
            throw new ArgumentException("ToUtc must be later than FromUtc.", nameof(filter));
        Required(filter.EvaluationVersion, nameof(filter.EvaluationVersion), 80);
        if (filter.FixtureId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(filter), "FixtureId must be positive.");
        if (filter.AfterSourceEvaluationId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(filter), "AfterSourceEvaluationId must be positive.");
        if (filter.AfterPredictionTimestampUtc.HasValue != filter.AfterSourceEvaluationId.HasValue)
            throw new ArgumentException(
                "Both AfterPredictionTimestampUtc and AfterSourceEvaluationId are required for a pagination cursor.",
                nameof(filter));
        if (filter.AfterPredictionTimestampUtc.HasValue
            && EnsureUtc(filter.AfterPredictionTimestampUtc.Value) >= EnsureUtc(filter.ToUtc))
            throw new ArgumentException("The pagination cursor must be earlier than ToUtc.", nameof(filter));
    }

    private static void Validate(RobustResidualHistoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Required(query.MarketFamily, nameof(query.MarketFamily), 30);
        if (query.OutcomeAvailabilityLagHours is < 0 or > 168)
            throw new ArgumentOutOfRangeException(nameof(query), "OutcomeAvailabilityLagHours must be between 0 and 168.");
        if (query.MaximumRows is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(query), "MaximumRows must be between 1 and 100000.");
        if (EnsureUtc(query.EvaluationAsOfUtc) > DateTime.UtcNow.AddMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(query), "EvaluationAsOfUtc cannot be in the future.");
    }

    private static void Validate(AppendRobustPolicyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Required(command.PolicyVersion, nameof(command.PolicyVersion), 120);
        Required(command.CreatedBy, nameof(command.CreatedBy), 120);
        if (command.EvaluationMode is not ("Shadow" or "Enforce" or "Disabled"))
            throw new ArgumentException("EvaluationMode must be Shadow, Enforce or Disabled.", nameof(command));
        if (command.Side is not null && command.Side is not ("Over" or "Under"))
            throw new ArgumentException("Side must be Over or Under when supplied.", nameof(command));
        if (command.MinimumLine > command.MaximumLine || command.MinimumOdds > command.MaximumOdds)
            throw new ArgumentException("Policy ranges are invalid.", nameof(command));
        if (command.MinimumLine is < 0 || command.MaximumLine is < 0
            || (command.MinimumOdds.HasValue && command.MinimumOdds <= 1m)
            || (command.MaximumOdds.HasValue && command.MaximumOdds <= 1m))
            throw new ArgumentException("Policy lines must be non-negative and decimal odds must exceed one.", nameof(command));
        _ = CanonicalizeJson(command.ConfigurationJson, "policy configuration");
        RequireJsonKind(command.ConfigurationJson, JsonValueKind.Object, "policy configuration");
    }

    private static void Validate(RobustPolicyQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (EnsureUtc(query.AsOfUtc) > DateTime.UtcNow.AddMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(query), "AsOfUtc cannot be in the future.");
        if (query.Line is < 0 || (query.Odds.HasValue && query.Odds <= 1m))
            throw new ArgumentException("Policy-query line or odds are invalid.", nameof(query));
    }

    private static void Required(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
            throw new ArgumentException($"{name} is required and must be at most {maximumLength} characters.", name);
    }

    private static void RequirePositive(long value, string name)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(name, "The identifier must be positive.");
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime? AsNullableUtc(DateTime? value) => value.HasValue ? EnsureUtc(value.Value) : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Invariant(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string CanonicalizeJson(string? json, string description)
    {
        if (string.IsNullOrWhiteSpace(json)) return description.Contains("reasons", StringComparison.OrdinalIgnoreCase)
            || description.Contains("warnings", StringComparison.OrdinalIgnoreCase)
            || description.Contains("histogram", StringComparison.OrdinalIgnoreCase) ? "[]" : "{}";
        try
        {
            return CanonicalizeNode(JsonNode.Parse(json)!);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The {description} must be valid JSON.", description, exception);
        }
    }

    private static void RequireJsonKind(string? json, JsonValueKind expected, string description)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json)
                ? expected == JsonValueKind.Array ? "[]" : "{}"
                : json);
            if (document.RootElement.ValueKind != expected)
                throw new ArgumentException($"The {description} must be a JSON {expected.ToString().ToLowerInvariant()}.", description);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The {description} must be valid JSON.", description, exception);
        }
    }

    private static void RequireJsonContainer(string? json, string description)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (document.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                throw new ArgumentException($"The {description} must be a JSON object or array.", description);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The {description} must be valid JSON.", description, exception);
        }
    }

    private static string CanonicalizeNode(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, node);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (var property in value.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (var item in value) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static RobustEvaluationMetricsDto CopyWithReasons(
        RobustEvaluationMetricsDto value,
        IReadOnlyList<RobustReasonMetricDto> reasons) => new()
    {
        Evaluated = value.Evaluated,
        ShadowApproved = value.ShadowApproved,
        ShadowRejected = value.ShadowRejected,
        ShadowReducedStake = value.ShadowReducedStake,
        ShadowManualReview = value.ShadowManualReview,
        DecisionDisagreements = value.DecisionDisagreements,
        Resolved = value.Resolved,
        BaselineStake = value.BaselineStake,
        BaselineProfitLoss = value.BaselineProfitLoss,
        BaselineYield = value.BaselineYield,
        RobustShadowStake = value.RobustShadowStake,
        RobustShadowProfitLoss = value.RobustShadowProfitLoss,
        RobustShadowYield = value.RobustShadowYield,
        RobustMaximumDrawdown = value.RobustMaximumDrawdown,
        AverageClvOdds = value.AverageClvOdds,
        AverageRobustnessScore = value.AverageRobustnessScore,
        AveragePointEdge = value.AveragePointEdge,
        AverageRobustEdge = value.AverageRobustEdge,
        AveragePointExpectedValue = value.AveragePointExpectedValue,
        AverageRobustExpectedValue = value.AverageRobustExpectedValue,
        Reasons = reasons
    };

    private sealed class PolicyAppendRow
    {
        public long RobustPolicyId { get; init; }
        public bool Inserted { get; init; }
    }

    private const string MetricsSql = """
        SELECT evaluation.*,
               selection.Status,
               SettledStake = selection.Stake,
               SettledProfitLoss = selection.ProfitLoss,
               selection.SettledAtUtc
        INTO #EvaluationScope
        FROM dbo.AutomatedBotPickRobustEvaluations AS evaluation
        LEFT JOIN dbo.AutomatedCornerBetSelections AS selection
          ON selection.AutomatedCornerBetSelectionId = evaluation.BotPickSelectionId
        WHERE evaluation.IsCurrent = 1
          AND (@FromUtc IS NULL OR evaluation.AsOfUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR evaluation.AsOfUtc < @ToUtc)
          AND (@BotKey IS NULL OR evaluation.BotKey = @BotKey)
          AND (@MarketFamily IS NULL OR evaluation.MarketFamily = @MarketFamily)
          AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
          AND (@EvaluationVersion IS NULL OR evaluation.EvaluationVersion = @EvaluationVersion);

        WITH Economics AS
        (
            SELECT *,
                IsResolved = CASE WHEN SettledProfitLoss IS NOT NULL AND SettledStake > 0 THEN 1 ELSE 0 END,
                BaselineProfit = CASE
                    WHEN SettledProfitLoss IS NOT NULL AND SettledStake > 0
                    THEN SettledProfitLoss / SettledStake * OriginalStake
                END,
                RobustProfit = CASE
                    WHEN SettledProfitLoss IS NOT NULL AND SettledStake > 0
                     AND RobustDecision IN (N'Approve', N'ReduceStake')
                    THEN SettledProfitLoss / SettledStake * RecommendedStake
                END
            FROM #EvaluationScope
        ),
        OrderedRobust AS
        (
            SELECT RobustEvaluationId, AsOfUtc, SettledAtUtc,
                CumulativeProfit = SUM(COALESCE(RobustProfit, 0)) OVER
                    (ORDER BY COALESCE(SettledAtUtc, AsOfUtc), RobustEvaluationId ROWS UNBOUNDED PRECEDING)
            FROM Economics
            WHERE IsResolved = 1
        ),
        Drawdown AS
        (
            SELECT CumulativeProfit,
                PriorPeak = CASE
                    WHEN MAX(CumulativeProfit) OVER
                        (ORDER BY COALESCE(SettledAtUtc, AsOfUtc), RobustEvaluationId ROWS UNBOUNDED PRECEDING) < 0
                    THEN 0
                    ELSE MAX(CumulativeProfit) OVER
                        (ORDER BY COALESCE(SettledAtUtc, AsOfUtc), RobustEvaluationId ROWS UNBOUNDED PRECEDING)
                END
            FROM OrderedRobust
        )
        SELECT
            Evaluated = COUNT_BIG(1),
            ShadowApproved = COALESCE(SUM(CASE WHEN RobustDecision = N'Approve' THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            ShadowRejected = COALESCE(SUM(CASE WHEN RobustDecision = N'Reject' THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            ShadowReducedStake = COALESCE(SUM(CASE WHEN RobustDecision = N'ReduceStake' THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            ShadowManualReview = COALESCE(SUM(CASE WHEN RobustDecision = N'ManualReview' THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            DecisionDisagreements = COALESCE(SUM(CASE
                WHEN CurrentSystemDecision IN (N'BET', N'Approved', N'Publish')
                 AND RobustDecision IN (N'Reject', N'ManualReview') THEN CONVERT(BIGINT, 1)
                WHEN CurrentSystemDecision NOT IN (N'BET', N'Approved', N'Publish')
                 AND RobustDecision IN (N'Approve', N'ReduceStake') THEN CONVERT(BIGINT, 1)
                ELSE 0 END), 0),
            Resolved = COALESCE(SUM(CONVERT(BIGINT, IsResolved)), 0),
            BaselineStake = SUM(CASE WHEN IsResolved = 1 THEN OriginalStake END),
            BaselineProfitLoss = SUM(BaselineProfit),
            BaselineYield = SUM(BaselineProfit) / NULLIF(SUM(CASE WHEN IsResolved = 1 THEN OriginalStake END), 0),
            RobustShadowStake = SUM(CASE WHEN IsResolved = 1 AND RobustDecision IN (N'Approve', N'ReduceStake') THEN RecommendedStake END),
            RobustShadowProfitLoss = SUM(RobustProfit),
            RobustShadowYield = SUM(RobustProfit) / NULLIF(SUM(CASE WHEN IsResolved = 1 AND RobustDecision IN (N'Approve', N'ReduceStake') THEN RecommendedStake END), 0),
            RobustMaximumDrawdown = (SELECT MAX(PriorPeak - CumulativeProfit) FROM Drawdown),
            AverageClvOdds = AVG(ClvOdds),
            AverageRobustnessScore = AVG(RobustnessScore),
            AveragePointEdge = AVG(PointEdge),
            AverageRobustEdge = AVG(RobustEdge),
            AveragePointExpectedValue = AVG(PointExpectedValue),
            AverageRobustExpectedValue = AVG(RobustExpectedValue)
        FROM Economics;

        SELECT
            ReasonCode = reason.[value],
            Occurrences = COUNT_BIG(1)
        FROM #EvaluationScope AS evaluation
        CROSS APPLY OPENJSON(evaluation.RejectionReasonCodesJson) AS reason
        WHERE reason.[type] = 1
        GROUP BY reason.[value]
        ORDER BY COUNT_BIG(1) DESC, reason.[value];
        """;

    private const string BackfillPreviewSql = """
        WITH DateScope AS
        (
            SELECT evaluation.AutomatedBotPickEvaluationId
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
            WHERE evaluation.PredictionTimestampUtc >= @FromUtc
              AND evaluation.PredictionTimestampUtc < @ToUtc

            UNION ALL

            SELECT evaluation.AutomatedBotPickEvaluationId
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
            WHERE evaluation.PredictionTimestampUtc IS NULL
              AND evaluation.EvaluatedAtUtc >= @FromUtc
              AND evaluation.EvaluatedAtUtc < @ToUtc
        ),
        SourceScope AS
        (
            SELECT
                evaluation.AutomatedBotPickEvaluationId,
                PredictionTimestampUtc = evaluation.PredictionTimestampUtc,
                evaluation.BaseModelTrainedThroughUtc,
                evaluation.OddsSnapshotId,
                ExactOddsSnapshotId = odds.CornerOddsSnapshotId,
                OddsCapturedAtUtc = odds.CapturedAtUtc,
                odds.OverOdds,
                odds.UnderOdds,
                evaluation.SelectedSide,
                evaluation.LineValue,
                PrimaryPrediction = COALESCE(
                    evaluation.Prediction2026,
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.features.prediction2026')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.model.basePredictedValue')),
                    evaluation.LegacyPrediction),
                RawProbability = COALESCE(
                    evaluation.BaseRawProbability,
                    evaluation.CandidateProbability,
                    evaluation.FinalProbability),
                CalibratedProbability = COALESCE(
                    evaluation.BaseCalibratedProbability,
                    evaluation.CalibratedProbability,
                    evaluation.FinalProbability),
                evaluation.DataQualityScore,
                evaluation.BaseModelVersion,
                evaluation.FeatureSnapshotJson,
                evaluation.MatchDate,
                AlreadyEvaluated = CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.AutomatedBotPickRobustEvaluations AS robust
                    WHERE robust.SourceEvaluationId = evaluation.AutomatedBotPickEvaluationId
                      AND robust.EvaluationVersion = @EvaluationVersion
                ) THEN 1 ELSE 0 END
            FROM DateScope AS dateScope
            INNER JOIN dbo.AutomatedBotPickEvaluations AS evaluation
              ON evaluation.AutomatedBotPickEvaluationId = dateScope.AutomatedBotPickEvaluationId
            LEFT JOIN dbo.CornerOddsSnapshots AS odds
              ON odds.CornerOddsSnapshotId = evaluation.OddsSnapshotId
            CROSS APPLY (VALUES
                (CASE WHEN ISJSON(evaluation.FeatureSnapshotJson) = 1
                      THEN evaluation.FeatureSnapshotJson ELSE N'{}' END)
            ) AS safe(FeatureJson)
            WHERE (@BotKey IS NULL OR evaluation.BotKey = @BotKey)
              AND (@MarketFamily IS NULL OR UPPER(COALESCE(NULLIF(evaluation.MarketFamily, N''),
                    CASE
                        WHEN evaluation.MarketType LIKE N'%Corners' THEN N'CORNERS'
                        WHEN evaluation.MarketType LIKE N'%ShotsOnGoal' THEN N'SOG'
                        WHEN evaluation.MarketType LIKE N'%Shots' THEN N'SHOTS'
                        WHEN evaluation.MarketType LIKE N'%Goals' THEN N'GOALS'
                    END)) = UPPER(@MarketFamily))
              AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
              AND (@FixtureId IS NULL OR COALESCE(NULLIF(evaluation.FixtureIdentity, 0),
                    NULLIF(evaluation.ApiFootballFixtureId, 0),
                    evaluation.PartidoProximoCuotaId) = @FixtureId)
        )
        SELECT
            DryRun = CONVERT(BIT, 1),
            SourceCandidates = COUNT_BIG(1),
            EligibleCandidates = COALESCE(SUM(CASE
                WHEN (@Force = 1 OR AlreadyEvaluated = 0)
                 AND PredictionTimestampUtc IS NOT NULL
                 AND BaseModelTrainedThroughUtc IS NOT NULL
                 AND BaseModelTrainedThroughUtc <= PredictionTimestampUtc
                 AND PredictionTimestampUtc < MatchDate
                 AND ExactOddsSnapshotId IS NOT NULL
                 AND OddsCapturedAtUtc <= PredictionTimestampUtc
                 AND OverOdds > 1 AND UnderOdds > 1
                 AND SelectedSide IN (N'Over', N'Under')
                 AND LineValue >= 0
                 AND PrimaryPrediction IS NOT NULL AND PrimaryPrediction >= 0
                 AND RawProbability BETWEEN 0 AND 1
                 AND CalibratedProbability BETWEEN 0 AND 1
                 AND DataQualityScore BETWEEN 0 AND 1
                 AND NULLIF(LTRIM(RTRIM(BaseModelVersion)), N'') IS NOT NULL
                 AND ISJSON(FeatureSnapshotJson) = 1
                THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            AlreadyEvaluated = COALESCE(SUM(CONVERT(BIGINT, AlreadyEvaluated)), 0),
            MissingPredictionTimestamp = COALESCE(SUM(CASE WHEN PredictionTimestampUtc IS NULL THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            MissingModelTrainingCutoff = COALESCE(SUM(CASE WHEN BaseModelTrainedThroughUtc IS NULL THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            ModelTrainedAfterPrediction = COALESCE(SUM(CASE WHEN BaseModelTrainedThroughUtc > PredictionTimestampUtc THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            MissingImmutableOddsSnapshot = COALESCE(SUM(CASE WHEN OddsSnapshotId IS NULL OR ExactOddsSnapshotId IS NULL THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            OddsSnapshotAfterPrediction = COALESCE(SUM(CASE WHEN OddsCapturedAtUtc > PredictionTimestampUtc THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            MissingBilateralOdds = COALESCE(SUM(CASE WHEN ExactOddsSnapshotId IS NOT NULL AND (OverOdds <= 1 OR UnderOdds <= 1 OR OverOdds IS NULL OR UnderOdds IS NULL) THEN CONVERT(BIGINT, 1) ELSE 0 END), 0),
            Message = CONVERT(NVARCHAR(2000), N'')
        FROM SourceScope
        OPTION (RECOMPILE);
        """;

    private const string BackfillCandidatesSql = """
        WITH CandidateSource AS
        (
            SELECT
                SourceEvaluationId = evaluation.AutomatedBotPickEvaluationId,
                evaluation.PublishedSelectionId,
                SourceOddsSnapshotId = odds.CornerOddsSnapshotId,
                FixtureId = COALESCE(NULLIF(evaluation.FixtureIdentity, 0),
                                     NULLIF(evaluation.ApiFootballFixtureId, 0),
                                     evaluation.PartidoProximoCuotaId),
                ExternalFixtureId = NULLIF(evaluation.ApiFootballFixtureId, 0),
                evaluation.PartidoProximoCuotaId,
                MatchDateUtc = evaluation.MatchDate,
                PredictionTimestampUtc = evaluation.PredictionTimestampUtc,
                OddsTimestampUtc = odds.CapturedAtUtc,
                evaluation.BotKey,
                evaluation.AutomationVersion,
                evaluation.Decision,
                evaluation.League,
                evaluation.HomeTeam,
                evaluation.AwayTeam,
                Bookmaker = COALESCE(NULLIF(evaluation.Bookmaker, N''), odds.Source, evaluation.Source),
                odds.SourceMatchId,
                MarketFamily = UPPER(COALESCE(NULLIF(evaluation.MarketFamily, N''),
                    CASE
                        WHEN evaluation.MarketType LIKE N'%Corners' THEN N'CORNERS'
                        WHEN evaluation.MarketType LIKE N'%ShotsOnGoal' THEN N'SOG'
                        WHEN evaluation.MarketType LIKE N'%Shots' THEN N'SHOTS'
                        WHEN evaluation.MarketType LIKE N'%Goals' THEN N'GOALS'
                    END)),
                evaluation.SourceMarketType,
                evaluation.MarketType,
                Side = evaluation.SelectedSide,
                Line = evaluation.LineValue,
                SelectedOdds = CASE evaluation.SelectedSide
                    WHEN N'Over' THEN odds.OverOdds
                    WHEN N'Under' THEN odds.UnderOdds
                END,
                OverOdds = odds.OverOdds,
                UnderOdds = odds.UnderOdds,
                PrimaryPrediction = COALESCE(
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.model.BasePredictedValue')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.model.basePredictedValue')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.features.prediction2026')),
                    CASE WHEN evaluation.Prediction2026 IS NOT NULL AND evaluation.LegacyPrediction IS NOT NULL
                         THEN (evaluation.Prediction2026 + evaluation.LegacyPrediction) / 2
                         ELSE evaluation.Prediction2026 END,
                    evaluation.LegacyPrediction),
                DirectPrediction = COALESCE(
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.model.BasePredictedValue')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.model.basePredictedValue')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.features.prediction2026')),
                    CASE WHEN evaluation.Prediction2026 IS NOT NULL AND evaluation.LegacyPrediction IS NOT NULL
                         THEN (evaluation.Prediction2026 + evaluation.LegacyPrediction) / 2
                         ELSE evaluation.Prediction2026 END,
                    evaluation.LegacyPrediction),
                ContextPrediction = COALESCE(
                    evaluation.ContextPrediction,
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.features.contextPrediction')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.context.contextExpected'))),
                HomePrediction = COALESCE(
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.features.homePrediction2026')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.context.expectedHome'))),
                AwayPrediction = COALESCE(
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.features.awayPrediction2026')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(safe.FeatureJson, '$.context.expectedAway'))),
                RawProbability = COALESCE(
                    evaluation.BaseRawProbability,
                    evaluation.CandidateProbability,
                    evaluation.FinalProbability),
                CalibratedProbability = COALESCE(
                    evaluation.ConservativeProbability,
                    evaluation.FinalProbability,
                    evaluation.CalibratedProbability,
                    evaluation.BaseCalibratedProbability),
                evaluation.ProbabilityLowerBound,
                evaluation.ProbabilityUpperBound,
                evaluation.DataQualityScore,
                OriginalStake = COALESCE(selection.Stake, evaluation.StakeUnits, CONVERT(DECIMAL(12,4), 0)),
                evaluation.BaseModelVersion,
                evaluation.BaseModelTrainedThroughUtc,
                SelectorVersion = evaluation.ConfigurationVersion,
                evaluation.CalibrationVersion,
                IntelligenceVersion = COALESCE(
                    JSON_VALUE(safe.FeatureJson, '$.footballIntelligence.Version'),
                    JSON_VALUE(safe.FeatureJson, '$.footballIntelligence.version'),
                    JSON_VALUE(safe.FeatureJson, '$.configuration.footballIntelligence.Version'),
                    JSON_VALUE(safe.FeatureJson, '$.configuration.footballIntelligence.version')),
                evaluation.FeatureSnapshotJson
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
            INNER JOIN dbo.CornerOddsSnapshots AS odds
              ON odds.CornerOddsSnapshotId = evaluation.OddsSnapshotId
            LEFT JOIN dbo.AutomatedCornerBetSelections AS selection
              ON selection.AutomatedCornerBetSelectionId = evaluation.PublishedSelectionId
            CROSS APPLY (VALUES
                (CASE WHEN ISJSON(evaluation.FeatureSnapshotJson) = 1
                      THEN evaluation.FeatureSnapshotJson ELSE N'{}' END)
            ) AS safe(FeatureJson)
            WHERE evaluation.PredictionTimestampUtc >= @FromUtc
              AND evaluation.PredictionTimestampUtc < @ToUtc
              AND evaluation.PredictionTimestampUtc < evaluation.MatchDate
              AND evaluation.BaseModelTrainedThroughUtc IS NOT NULL
              AND evaluation.BaseModelTrainedThroughUtc <= evaluation.PredictionTimestampUtc
              AND odds.CapturedAtUtc <= evaluation.PredictionTimestampUtc
              AND odds.OverOdds > 1 AND odds.UnderOdds > 1
              AND evaluation.SelectedSide IN (N'Over', N'Under')
              AND evaluation.LineValue >= 0
              AND evaluation.DataQualityScore BETWEEN 0 AND 1
              AND ISJSON(evaluation.FeatureSnapshotJson) = 1
              AND (@BotKey IS NULL OR evaluation.BotKey = @BotKey)
              AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
              AND (@FixtureId IS NULL OR COALESCE(NULLIF(evaluation.FixtureIdentity, 0),
                      NULLIF(evaluation.ApiFootballFixtureId, 0),
                      evaluation.PartidoProximoCuotaId) = @FixtureId)
              AND
              (
                  @AfterPredictionTimestampUtc IS NULL
                  OR evaluation.PredictionTimestampUtc > @AfterPredictionTimestampUtc
                  OR (evaluation.PredictionTimestampUtc = @AfterPredictionTimestampUtc
                      AND evaluation.AutomatedBotPickEvaluationId > @AfterSourceEvaluationId)
              )
              AND
              (
                  @Force = 1
                  OR NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.AutomatedBotPickRobustEvaluations AS robust
                      WHERE robust.SourceEvaluationId = evaluation.AutomatedBotPickEvaluationId
                        AND robust.EvaluationVersion = @EvaluationVersion
                  )
              )
        ),
        Eligible AS
        (
            SELECT *
            FROM CandidateSource
            WHERE (@MarketFamily IS NULL OR MarketFamily = @MarketFamily)
              AND PrimaryPrediction IS NOT NULL AND PrimaryPrediction >= 0
              AND SelectedOdds > 1
              AND RawProbability BETWEEN 0 AND 1
              AND CalibratedProbability BETWEEN 0 AND 1
              AND NULLIF(LTRIM(RTRIM(BaseModelVersion)), N'') IS NOT NULL
        )
        SELECT TOP (@BatchSize) *
        FROM Eligible
        ORDER BY PredictionTimestampUtc, SourceEvaluationId;
        """;

    private const string ResidualHistorySql = """
        WITH RejectedScopes AS
        (
            SELECT DISTINCT BotKey, ConfigurationVersion, MarketType
            FROM dbo.AutomatedBotPickEvaluations
            WHERE Decision IN (N'Rejected', N'Abstain', N'PendingData', N'Invalid')
        ),
        SourceCandidates AS
        (
            SELECT TOP (@MaximumRows * 4)
                SourceEvaluationId = evaluation.AutomatedBotPickEvaluationId,
                FixtureId = COALESCE(evaluation.FixtureIdentity, evaluation.ApiFootballFixtureId),
                evaluation.ApiFootballFixtureId,
                evaluation.PublishedSelectionId,
                evaluation.BotKey,
                MarketFamily = UPPER(COALESCE(NULLIF(evaluation.MarketFamily, N''),
                    CASE
                        WHEN evaluation.MarketType LIKE N'%Corners' THEN N'CORNERS'
                        WHEN evaluation.MarketType LIKE N'%ShotsOnGoal' THEN N'SOG'
                        WHEN evaluation.MarketType LIKE N'%Shots' THEN N'SHOTS'
                        WHEN evaluation.MarketType LIKE N'%Goals' THEN N'GOALS'
                    END)),
                evaluation.MarketType,
                Side = evaluation.SelectedSide,
                evaluation.League,
                FixtureStartUtc = evaluation.MatchDate,
                Line = evaluation.LineValue,
                Odds = evaluation.SelectedOdds,
                Prediction = COALESCE(
                    evaluation.Prediction2026,
                    evaluation.LegacyPrediction,
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(evaluation.FeatureSnapshotJson, '$.directPrediction')),
                    TRY_CONVERT(DECIMAL(18,6), JSON_VALUE(evaluation.FeatureSnapshotJson, '$.prediction'))),
                PredictionAsOfUtc = evaluation.PredictionTimestampUtc,
                ModelTrainedThroughUtc = evaluation.BaseModelTrainedThroughUtc,
                ModelVersion = evaluation.BaseModelVersion,
                -- Missing quality metadata is fail-closed, never an implicit
                -- neutral/positive 0.5 contribution.
                DataQualityScore = COALESCE(evaluation.DataQualityScore, CONVERT(DECIMAL(9,6), 0)),
                ResidualSource = CASE WHEN rejected.BotKey IS NOT NULL
                    THEN N'AllCandidates' ELSE N'SelectedPicksOnly' END
            FROM dbo.AutomatedBotPickEvaluations AS evaluation
            LEFT JOIN RejectedScopes AS rejected
              ON rejected.BotKey = evaluation.BotKey
             AND rejected.ConfigurationVersion = evaluation.ConfigurationVersion
             AND rejected.MarketType = evaluation.MarketType
            WHERE evaluation.PredictionTimestampUtc IS NOT NULL
              AND evaluation.PredictionTimestampUtc < @EvaluationAsOfUtc
              AND evaluation.PredictionTimestampUtc < evaluation.MatchDate
              AND evaluation.MatchDate < @EvaluationAsOfUtc
              AND evaluation.BaseModelTrainedThroughUtc IS NOT NULL
              AND evaluation.BaseModelTrainedThroughUtc <= evaluation.PredictionTimestampUtc
              AND evaluation.SelectedSide IN (N'Over', N'Under')
              AND
              (
                  evaluation.MarketFamily = @MarketFamily
                  OR
                  (
                      NULLIF(evaluation.MarketFamily, N'') IS NULL
                      AND CASE
                          WHEN evaluation.MarketType LIKE N'%Corners' THEN N'CORNERS'
                          WHEN evaluation.MarketType LIKE N'%ShotsOnGoal' THEN N'SOG'
                          WHEN evaluation.MarketType LIKE N'%Shots' THEN N'SHOTS'
                          WHEN evaluation.MarketType LIKE N'%Goals' THEN N'GOALS'
                      END = @MarketFamily
                  )
              )
              AND (@MarketType IS NULL OR evaluation.MarketType = @MarketType)
              AND (@Side IS NULL OR evaluation.SelectedSide = @Side)
              AND (@League IS NULL OR evaluation.League = @League)
            ORDER BY evaluation.PredictionTimestampUtc DESC,
                     evaluation.AutomatedBotPickEvaluationId DESC
        ),
        LinkedMatches AS
        (
            SELECT
                source.*,
                MatchHistoryId = CONVERT(BIGINT, history.Id),
                history.HomeGoals, history.AwayGoals,
                history.HomeCorners, history.AwayCorners,
                history.HomeShots, history.AwayShots,
                history.HomeShotsOnGoal, history.AwayShotsOnGoal,
                OutcomeAvailableUtc = COALESCE(sourceOutcome.OutcomeAvailableUtc, history.ApiFootballUpdatedAtUtc),
                MatchRank = ROW_NUMBER() OVER
                (
                    PARTITION BY source.SourceEvaluationId
                    ORDER BY
                        CASE WHEN history.ApiFootballFixtureId = source.ApiFootballFixtureId THEN 0 ELSE 1 END,
                        COALESCE(sourceOutcome.OutcomeAvailableUtc, history.ApiFootballUpdatedAtUtc),
                        history.Id DESC
                )
            FROM SourceCandidates AS source
            LEFT JOIN dbo.AutomatedCornerBetSelections AS selection
              ON selection.AutomatedCornerBetSelectionId = source.PublishedSelectionId
            LEFT JOIN dbo.AutomatedBotPickEvaluations AS sourceOutcome
              ON sourceOutcome.AutomatedBotPickEvaluationId = source.SourceEvaluationId
            INNER JOIN dbo.MatchHistory AS history
              ON (source.ApiFootballFixtureId IS NOT NULL
                  AND history.ApiFootballFixtureId = source.ApiFootballFixtureId)
               OR (selection.MatchHistoryId IS NOT NULL AND history.Id = selection.MatchHistoryId)
        ),
        Actuals AS
        (
            SELECT *, ActualResult = CONVERT(DECIMAL(18,6), CASE MarketType
                WHEN N'TotalGoals' THEN HomeGoals + AwayGoals
                WHEN N'HomeTeamGoals' THEN HomeGoals
                WHEN N'AwayTeamGoals' THEN AwayGoals
                WHEN N'TotalCorners' THEN HomeCorners + AwayCorners
                WHEN N'HomeTeamCorners' THEN HomeCorners
                WHEN N'AwayTeamCorners' THEN AwayCorners
                WHEN N'TotalShots' THEN HomeShots + AwayShots
                WHEN N'HomeTeamShots' THEN HomeShots
                WHEN N'AwayTeamShots' THEN AwayShots
                WHEN N'TotalShotsOnGoal' THEN HomeShotsOnGoal + AwayShotsOnGoal
                WHEN N'HomeTeamShotsOnGoal' THEN HomeShotsOnGoal
                WHEN N'AwayTeamShotsOnGoal' THEN AwayShotsOnGoal
            END)
            FROM LinkedMatches
            WHERE MatchRank = 1
              AND OutcomeAvailableUtc IS NOT NULL
              -- A real availability timestamp already includes the provider delay;
              -- OutcomeAvailabilityLagHours is only for a fixture-end fallback.
              AND OutcomeAvailableUtc <= @EvaluationAsOfUtc
        )
        SELECT TOP (@MaximumRows)
            SourceEvaluationId, MatchHistoryId, FixtureId, BotKey,
            MarketFamily, MarketType, Side, League, Line, Odds,
            Prediction, ActualResult,
            Residual = ActualResult - Prediction,
            FixtureStartUtc,
            FixtureEndUtc = OutcomeAvailableUtc,
            PredictionAsOfUtc, ModelTrainedThroughUtc, OutcomeAvailableUtc,
            ModelVersion, ResidualSource, DataQualityScore
        FROM Actuals
        WHERE MarketFamily = @MarketFamily
          AND Prediction IS NOT NULL AND ActualResult IS NOT NULL
          AND OutcomeAvailableUtc > PredictionAsOfUtc
          AND OutcomeAvailableUtc >= FixtureStartUtc
        ORDER BY PredictionAsOfUtc DESC, SourceEvaluationId DESC;
        """;

    private const string OpenExposureSql = """
        SELECT
            BotPickSelectionId = selection.AutomatedCornerBetSelectionId,
            FixtureId = COALESCE(selection.ApiFootballFixtureId, robust.FixtureId),
            selection.BotKey,
            MarketFamily = CASE
                WHEN selection.MarketType LIKE N'%Corners' THEN N'CORNERS'
                WHEN selection.MarketType LIKE N'%ShotsOnGoal' THEN N'SOG'
                WHEN selection.MarketType LIKE N'%Shots' THEN N'SHOTS'
                WHEN selection.MarketType LIKE N'%Goals' THEN N'GOALS'
                ELSE N'UNKNOWN' END,
            selection.MarketType,
            Side = selection.SelectedSide,
            selection.League,
            HomeTeam = COALESCE(NULLIF(selection.StandardizedHomeTeam, N''), selection.HomeTeam),
            AwayTeam = COALESCE(NULLIF(selection.StandardizedAwayTeam, N''), selection.AwayTeam),
            selection.MatchDate,
            selection.Stake,
            robust.RobustnessScore,
            CorrelationCluster = CONCAT(
                COALESCE(selection.ApiFootballFixtureId, robust.FixtureId,
                         selection.AutomatedCornerBetSelectionId),
                N'|',
                CASE selection.SelectedSide
                    WHEN N'Over' THEN N'HIGH_EVENT'
                    WHEN N'Under' THEN N'LOW_EVENT'
                    ELSE N'NEUTRAL' END)
        FROM dbo.AutomatedCornerBetSelections AS selection
        OUTER APPLY
        (
            SELECT TOP (1) evaluation.RobustnessScore, evaluation.FixtureId
            FROM dbo.AutomatedBotPickRobustEvaluations AS evaluation
            WHERE evaluation.BotPickSelectionId = selection.AutomatedCornerBetSelectionId
              AND evaluation.AsOfUtc <= @AsOfUtc
            ORDER BY evaluation.AsOfUtc DESC, evaluation.RobustEvaluationId DESC
        ) AS robust
        WHERE selection.CreatedAtUtc <= @AsOfUtc
          AND (selection.SettledAtUtc IS NULL OR selection.SettledAtUtc > @AsOfUtc)
          AND (selection.Status <> N'Void' OR selection.SettledAtUtc > @AsOfUtc);
        """;

    private const string PolicyQuerySql = """
        SELECT TOP (1)
            policy.*,
            Specificity =
                CASE WHEN policy.BotKey IS NULL THEN 0 ELSE 128 END
              + CASE WHEN policy.MarketFamily IS NULL THEN 0 ELSE 64 END
              + CASE WHEN policy.MarketType IS NULL THEN 0 ELSE 32 END
              + CASE WHEN policy.MarketScope IS NULL THEN 0 ELSE 16 END
              + CASE WHEN policy.Side IS NULL THEN 0 ELSE 8 END
              + CASE WHEN policy.LeaguePattern IS NULL THEN 0 ELSE 4 END
              + CASE WHEN policy.MinimumLine IS NULL AND policy.MaximumLine IS NULL THEN 0 ELSE 2 END
              + CASE WHEN policy.MinimumOdds IS NULL AND policy.MaximumOdds IS NULL THEN 0 ELSE 1 END
        FROM dbo.AutomatedBotRobustPolicies AS policy
        WHERE policy.EffectiveFromUtc <= @AsOfUtc
          AND (policy.BotKey IS NULL OR policy.BotKey = @BotKey)
          AND (policy.MarketFamily IS NULL OR policy.MarketFamily = @MarketFamily)
          AND (policy.MarketType IS NULL OR policy.MarketType = @MarketType)
          AND (policy.MarketScope IS NULL OR policy.MarketScope = @MarketScope)
          AND (policy.Side IS NULL OR policy.Side = @Side)
          AND (policy.LeaguePattern IS NULL OR @League LIKE policy.LeaguePattern)
          AND (policy.MinimumLine IS NULL OR @Line >= policy.MinimumLine)
          AND (policy.MaximumLine IS NULL OR @Line <= policy.MaximumLine)
          AND (policy.MinimumOdds IS NULL OR @Odds >= policy.MinimumOdds)
          AND (policy.MaximumOdds IS NULL OR @Odds <= policy.MaximumOdds)
        """;
}
