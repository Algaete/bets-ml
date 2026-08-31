using System.Text.Json;
using CornersPrediction.Application.FootballIntelligence;
using CornersPrediction.Domain.FootballIntelligence;
using Microsoft.Extensions.Options;

namespace CornersPredictionApi.FootballIntelligence;

public sealed class MatchIntelligenceService : IMatchIntelligenceService
{
    private readonly IStructuredFootballDataProvider _footballData;
    private readonly INewsSearchProvider _newsSearch;
    private readonly INewsQueryBuilder _queryBuilder;
    private readonly IArticleContentExtractor _articleExtractor;
    private readonly IArticleDeduplicator _deduplicator;
    private readonly IRelevantTextSelector _textSelector;
    private readonly INewsFactExtractor _factExtractor;
    private readonly IEntityResolver _entityResolver;
    private readonly INewsFactConsolidator _consolidator;
    private readonly IIntelligenceSnapshotBuilder _snapshotBuilder;
    private readonly INewsDocumentRepository _documentRepository;
    private readonly INewsFactRepository _factRepository;
    private readonly IIntelligenceSnapshotRepository _snapshotRepository;
    private readonly IFootballSourceRepository _sourceRepository;
    private readonly ITeamAliasRepository _teamAliasRepository;
    private readonly IMatchIntelligenceRunRepository _runRepository;
    private readonly FootballIntelligenceOptions _options;
    private readonly ILogger<MatchIntelligenceService> _logger;

    public MatchIntelligenceService(
        IStructuredFootballDataProvider footballData,
        INewsSearchProvider newsSearch,
        INewsQueryBuilder queryBuilder,
        IArticleContentExtractor articleExtractor,
        IArticleDeduplicator deduplicator,
        IRelevantTextSelector textSelector,
        INewsFactExtractor factExtractor,
        IEntityResolver entityResolver,
        INewsFactConsolidator consolidator,
        IIntelligenceSnapshotBuilder snapshotBuilder,
        INewsDocumentRepository documentRepository,
        INewsFactRepository factRepository,
        IIntelligenceSnapshotRepository snapshotRepository,
        IFootballSourceRepository sourceRepository,
        ITeamAliasRepository teamAliasRepository,
        IMatchIntelligenceRunRepository runRepository,
        IOptions<FootballIntelligenceOptions> options,
        ILogger<MatchIntelligenceService> logger)
    {
        _footballData = footballData;
        _newsSearch = newsSearch;
        _queryBuilder = queryBuilder;
        _articleExtractor = articleExtractor;
        _deduplicator = deduplicator;
        _textSelector = textSelector;
        _factExtractor = factExtractor;
        _entityResolver = entityResolver;
        _consolidator = consolidator;
        _snapshotBuilder = snapshotBuilder;
        _documentRepository = documentRepository;
        _factRepository = factRepository;
        _snapshotRepository = snapshotRepository;
        _sourceRepository = sourceRepository;
        _teamAliasRepository = teamAliasRepository;
        _runRepository = runRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MatchIntelligenceResult> RunAsync(
        RunMatchIntelligenceCommand command,
        CancellationToken cancellationToken)
    {
        if (command.FixtureId <= 0)
            throw new ArgumentException("FixtureId must be positive.");
        if (!_options.Enabled)
        {
            return new MatchIntelligenceResult(
                command.FixtureId,
                EnsureUtc(command.CutoffUtc),
                null,
                PreMatchDecision.Keep,
                ["FootballIntelligenceDisabled"],
                0,
                0,
                0,
                "Disabled");
        }
        var requestedCutoff = EnsureUtc(command.CutoffUtc);
        var startedAt = DateTime.UtcNow;
        if (requestedCutoff > startedAt.AddMinutes(1))
            throw new ArgumentException("CutoffUtc cannot be in the future.");
        var isLiveRequest = Math.Abs((startedAt - requestedCutoff).TotalMinutes) <= 1d;
        var cutoff = isLiveRequest ? startedAt : requestedCutoff;
        var previousRun = await _runRepository.GetAsync(command.FixtureId, cutoff, cancellationToken);
        if (!command.ForceRefresh && previousRun?.Status is MatchIntelligenceRunStatus.Completed
            or MatchIntelligenceRunStatus.CompletedWithoutEvidence)
        {
            return await GetLatestAsync(command.FixtureId, cutoff, cancellationToken)
                ?? new MatchIntelligenceResult(command.FixtureId, cutoff, null, PreMatchDecision.Keep,
                    ["CompletedRunWithoutSnapshot"], 0, 0, 0, "CompletedWithoutEvidence");
        }

        var run = new MatchIntelligenceRun
        {
            FixtureId = command.FixtureId,
            CutoffAtUtc = cutoff,
            StartedAtUtc = startedAt,
            Status = MatchIntelligenceRunStatus.Running,
            CreatedAtUtc = startedAt
        };
        var runId = await _runRepository.StartAsync(run, cancellationToken);
        var counters = new RunCounters();
        try
        {
            var fixture = await _footballData.GetFixtureAsync(command.FixtureId, cancellationToken)
                ?? throw new InvalidOperationException($"API-Football did not return fixture {command.FixtureId}.");
            if (cutoff > EnsureUtc(fixture.KickoffUtc))
                throw new ArgumentException("CutoffUtc cannot be after kickoff for a pre-match snapshot.");

            var canIngestCurrentEvidence = startedAt <= cutoff.AddSeconds(1);
            IReadOnlyCollection<InjuryDto> injuries = [];
            IReadOnlyCollection<FixtureLineupDto> lineups = [];
            IReadOnlyCollection<SquadPlayerDto> homeSquad = [];
            IReadOnlyCollection<SquadPlayerDto> awaySquad = [];
            if (canIngestCurrentEvidence)
            {
                var injuriesTask = _footballData.GetFixtureInjuriesAsync(fixture.FixtureId, cancellationToken);
                var lineupsTask = _footballData.GetFixtureLineupsAsync(fixture.FixtureId, cancellationToken);
                var homeSquadTask = _footballData.GetSquadAsync(fixture.Home.TeamId, cancellationToken);
                var awaySquadTask = _footballData.GetSquadAsync(fixture.Away.TeamId, cancellationToken);
                await Task.WhenAll(injuriesTask, lineupsTask, homeSquadTask, awaySquadTask);
                injuries = await injuriesTask;
                lineups = await lineupsTask;
                homeSquad = await homeSquadTask;
                awaySquad = await awaySquadTask;

                await PersistStructuredEvidenceAsync(fixture, fixture.Home, homeSquad, injuries, lineups, startedAt, counters, cancellationToken);
                await PersistStructuredEvidenceAsync(fixture, fixture.Away, awaySquad, injuries, lineups, startedAt, counters, cancellationToken);
                await ProcessNewsAsync(fixture, fixture.Home, fixture.Away, homeSquad, cutoff, startedAt, counters, cancellationToken);
                await ProcessNewsAsync(fixture, fixture.Away, fixture.Home, awaySquad, cutoff, startedAt, counters, cancellationToken);
            }

            var documents = await _documentRepository.GetByFixtureAsync(fixture.FixtureId, cutoff, cancellationToken);
            var persistedFacts = await _factRepository.GetByFixtureAsync(fixture.FixtureId, cutoff, cancellationToken);
            var facts = _consolidator.Consolidate(persistedFacts, cutoff);
            var homeSnapshot = _snapshotBuilder.Build(
                fixture,
                fixture.Home,
                true,
                cutoff,
                documents,
                facts,
                homeSquad,
                lineups.SingleOrDefault(value => value.TeamId == fixture.Home.TeamId));
            var awaySnapshot = _snapshotBuilder.Build(
                fixture,
                fixture.Away,
                false,
                cutoff,
                documents,
                facts,
                awaySquad,
                lineups.SingleOrDefault(value => value.TeamId == fixture.Away.TeamId));
            var homeId = await _snapshotRepository.UpsertAsync(homeSnapshot, cancellationToken);
            var awayId = await _snapshotRepository.UpsertAsync(awaySnapshot, cancellationToken);
            homeSnapshot = homeSnapshot with { Id = homeId };
            awaySnapshot = awaySnapshot with { Id = awayId };
            var pair = new MatchIntelligenceSnapshotPair(fixture.FixtureId, homeSnapshot, awaySnapshot);
            var actionable = homeSnapshot.ActionableFactCount + awaySnapshot.ActionableFactCount;
            var status = actionable > 0
                ? MatchIntelligenceRunStatus.Completed
                : MatchIntelligenceRunStatus.CompletedWithoutEvidence;
            var conflicts = homeSnapshot.ConflictCount + awaySnapshot.ConflictCount;
            await CompleteRunAsync(run with
            {
                Id = runId,
                FinishedAtUtc = DateTime.UtcNow,
                Status = status,
                QueriesGenerated = counters.Queries,
                SearchResults = counters.SearchResults,
                DocumentsDownloaded = counters.DocumentsDownloaded,
                DocumentsProcessed = documents.Count,
                FactsExtracted = facts.Count,
                ResolvedFacts = facts.Count(value => value.ResolutionStatus is EntityResolutionStatus.ResolvedExact
                    or EntityResolutionStatus.ResolvedAlias or EntityResolutionStatus.ResolvedFuzzy),
                UnresolvedFacts = facts.Count(value => value.ResolutionStatus is EntityResolutionStatus.NotFound
                    or EntityResolutionStatus.Ambiguous),
                ConflictCount = conflicts,
                LlmTokensInput = counters.InputTokens,
                LlmTokensOutput = counters.OutputTokens
            }, cancellationToken);

            return new MatchIntelligenceResult(
                fixture.FixtureId,
                cutoff,
                pair,
                conflicts > 0 ? PreMatchDecision.ReduceConfidence : PreMatchDecision.Keep,
                actionable > 0 ? ["ActionablePreMatchEvidence"] : ["NoActionablePreMatchEvidence"],
                documents.Count,
                facts.Count,
                conflicts,
                status.ToString());
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Football intelligence run failed for FixtureId={FixtureId}, Cutoff={Cutoff}", command.FixtureId, cutoff);
            await CompleteRunAsync(run with
            {
                Id = runId,
                FinishedAtUtc = DateTime.UtcNow,
                Status = MatchIntelligenceRunStatus.Failed,
                QueriesGenerated = counters.Queries,
                SearchResults = counters.SearchResults,
                DocumentsDownloaded = counters.DocumentsDownloaded,
                FactsExtracted = counters.Facts,
                LlmTokensInput = counters.InputTokens,
                LlmTokensOutput = counters.OutputTokens,
                ErrorMessage = exception.Message
            }, CancellationToken.None);
            throw;
        }
    }

    public async Task<MatchIntelligenceResult?> GetLatestAsync(
        long fixtureId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var cutoff = EnsureUtc(cutoffUtc);
        var pair = await _snapshotRepository.GetLatestPairAsync(fixtureId, cutoff, cancellationToken);
        if (pair is null)
            return null;
        var documents = await _documentRepository.GetByFixtureAsync(fixtureId, cutoff, cancellationToken);
        var facts = await _factRepository.GetByFixtureAsync(fixtureId, cutoff, cancellationToken);
        var conflicts = (pair.Home?.ConflictCount ?? 0) + (pair.Away?.ConflictCount ?? 0);
        var actionable = (pair.Home?.ActionableFactCount ?? 0) + (pair.Away?.ActionableFactCount ?? 0);
        return new MatchIntelligenceResult(
            fixtureId,
            cutoff,
            pair,
            conflicts > 0 ? PreMatchDecision.ReduceConfidence : PreMatchDecision.Keep,
            actionable > 0 ? ["ActionablePreMatchEvidence"] : ["NoActionablePreMatchEvidence"],
            documents.Count,
            facts.Count,
            conflicts,
            actionable > 0 ? "Completed" : "CompletedWithoutEvidence");
    }

    private async Task PersistStructuredEvidenceAsync(
        IntelligenceFixtureDto fixture,
        IntelligenceTeamDto team,
        IReadOnlyCollection<SquadPlayerDto> squad,
        IReadOnlyCollection<InjuryDto> injuries,
        IReadOnlyCollection<FixtureLineupDto> lineups,
        DateTime observedAtUtc,
        RunCounters counters,
        CancellationToken cancellationToken)
    {
        var teamInjuries = injuries.Where(value => value.TeamId == team.TeamId).ToArray();
        var lineup = lineups.SingleOrDefault(value => value.TeamId == team.TeamId);
        if (teamInjuries.Length == 0 && lineup is null)
            return;
        var url = $"api-football://fixture/{fixture.FixtureId}/team/{team.TeamId}";
        var payload = JsonSerializer.Serialize(new { injuries = teamInjuries, lineup });
        var contentHash = FootballIntelligenceHash.Sha256(payload);
        var document = new FootballNewsDocument
        {
            FixtureId = fixture.FixtureId,
            TeamId = team.TeamId,
            Url = url,
            CanonicalUrl = url,
            UrlHash = FootballIntelligenceHash.Sha256($"{url}|{contentHash}"),
            ContentHash = contentHash,
            SourceDomain = "api-football.com",
            SourceTier = NewsSourceTier.StructuredProvider,
            Title = $"API-Football structured pre-match evidence: {team.Name}",
            PublishedAtUtc = observedAtUtc,
            FirstSeenAtUtc = observedAtUtc,
            RetrievedAtUtc = observedAtUtc,
            NormalizedText = payload,
            ExtractionStatus = NewsExtractionStatus.Extracted,
            HttpStatusCode = 200,
            CreatedAtUtc = observedAtUtc
        };
        var documentId = await _documentRepository.UpsertAsync(document, cancellationToken);
        counters.DocumentsDownloaded++;
        var players = squad.ToDictionary(value => value.PlayerId);
        var facts = new List<FootballNewsFact>();
        foreach (var injury in teamInjuries)
        {
            players.TryGetValue(injury.PlayerId, out var player);
            var isSuspension = StructuredFootballEvidenceClassifier.IsSuspension(
                injury.Type,
                injury.Reason);
            facts.Add(CreateFact(
                documentId,
                fixture.FixtureId,
                team.TeamId,
                injury.PlayerId,
                team.Name,
                injury.PlayerName,
                player?.Position,
                isSuspension ? FootballNewsEventType.Suspension : FootballNewsEventType.Injury,
                isSuspension ? AvailabilityStatus.Suspended : AvailabilityStatus.ConfirmedOut,
                FactCertainty.Confirmed,
                0m,
                injury.Reason ?? injury.Type ?? "API-Football injury",
                observedAtUtc,
                0.95m,
                EntityResolutionStatus.ResolvedExact,
                "API-Football"));
        }
        if (lineup is not null)
        {
            foreach (var player in lineup.Players)
            {
                facts.Add(CreateFact(
                    documentId,
                    fixture.FixtureId,
                    team.TeamId,
                    player.PlayerId,
                    team.Name,
                    player.Name,
                    player.Position,
                    player.IsStarter ? FootballNewsEventType.OfficialStarter : FootballNewsEventType.OfficialBench,
                    player.IsStarter ? AvailabilityStatus.Starting : AvailabilityStatus.Bench,
                    FactCertainty.Confirmed,
                    1m,
                    player.IsStarter ? "Official starting lineup" : "Official bench",
                    observedAtUtc,
                    1m,
                    EntityResolutionStatus.ResolvedExact,
                    "API-Football"));
            }
        }
        await _factRepository.InsertAsync(facts, cancellationToken);
        counters.Facts += facts.Count;
    }

    private async Task ProcessNewsAsync(
        IntelligenceFixtureDto fixture,
        IntelligenceTeamDto team,
        IntelligenceTeamDto opponent,
        IReadOnlyCollection<SquadPlayerDto> squad,
        DateTime cutoff,
        DateTime firstSeenAtUtc,
        RunCounters counters,
        CancellationToken cancellationToken)
    {
        var aliases = await _teamAliasRepository.GetAliasesAsync(team.TeamId, cancellationToken);
        var queries = _queryBuilder.Build(team.Name, opponent.Name, aliases, ["en", "es", "pt"])
            .Take(Math.Clamp(_options.MaximumQueriesPerTeam, 1, 30))
            .ToArray();
        var searchResults = new List<NewsSearchResult>();
        foreach (var query in queries)
        {
            counters.Queries++;
            try
            {
                var language = query.Contains(" lesion", StringComparison.OrdinalIgnoreCase)
                    || query.Contains(" aline", StringComparison.OrdinalIgnoreCase) ? "es"
                    : query.Contains(" desfal", StringComparison.OrdinalIgnoreCase)
                      || query.Contains(" escala", StringComparison.OrdinalIgnoreCase) ? "pt" : "en";
                var rows = await _newsSearch.SearchAsync(
                    new NewsSearchRequest(fixture.FixtureId, team.TeamId, team.Name, opponent.Name,
                        query, cutoff, language, 5),
                    cancellationToken);
                searchResults.AddRange(rows.Where(value =>
                    !value.PublishedAtUtc.HasValue || EnsureUtc(value.PublishedAtUtc.Value) <= cutoff));
                counters.SearchResults += rows.Count;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception,
                    "News search failed for FixtureId={FixtureId}, TeamId={TeamId}, Query={Query}",
                    fixture.FixtureId,
                    team.TeamId,
                    query);
            }
        }

        var extracted = new List<ExtractedArticle>();
        foreach (var result in searchResults
                     .GroupBy(value => value.Url.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .Take(Math.Clamp(_options.MaximumArticlesPerTeam, 1, 30)))
        {
            var article = await _articleExtractor.ExtractAsync(result.Url, cancellationToken);
            if (article is not null
                && (!article.PublishedAtUtc.HasValue || EnsureUtc(article.PublishedAtUtc.Value) <= cutoff))
                extracted.Add(article);
        }

        foreach (var article in _deduplicator.Deduplicate(extracted))
        {
            var selectedText = _textSelector.Select(
                article.NormalizedText,
                team.Name,
                opponent.Name,
                squad.Select(value => value.Name).ToArray(),
                _options.ArticleMaxCharacters);
            if (string.IsNullOrWhiteSpace(selectedText))
                continue;
            var source = await ResolveSourceAsync(article.Domain, cancellationToken);
            if (!source.IsEnabled)
                continue;
            var canonicalUrl = article.CanonicalUrl?.AbsoluteUri ?? article.Url.AbsoluteUri;
            var document = new FootballNewsDocument
            {
                FixtureId = fixture.FixtureId,
                TeamId = team.TeamId,
                Url = article.Url.AbsoluteUri,
                CanonicalUrl = article.CanonicalUrl?.AbsoluteUri,
                UrlHash = FootballIntelligenceHash.Sha256($"{canonicalUrl}|{article.ContentHash}"),
                ContentHash = article.ContentHash,
                SourceDomain = source.Domain,
                SourceTier = source.Tier,
                Title = article.Title,
                Author = article.Author,
                LanguageCode = article.Language,
                PublishedAtUtc = article.PublishedAtUtc,
                UpdatedAtUtc = article.UpdatedAtUtc,
                FirstSeenAtUtc = firstSeenAtUtc,
                RetrievedAtUtc = firstSeenAtUtc,
                NormalizedText = selectedText,
                ExtractionStatus = NewsExtractionStatus.Extracted,
                HttpStatusCode = article.HttpStatusCode,
                CreatedAtUtc = firstSeenAtUtc
            };
            var documentId = await _documentRepository.UpsertAsync(document, cancellationToken);
            counters.DocumentsDownloaded++;
            NewsExtractionResult extraction;
            try
            {
                extraction = await _factExtractor.ExtractAsync(
                    new NewsExtractionRequest(
                        fixture.FixtureId,
                        fixture.KickoffUtc,
                        cutoff,
                        team.Name,
                        opponent.Name,
                        article.Title,
                        article.PublishedAtUtc,
                        selectedText,
                        article.Language,
                        squad.Select(value => value.Name).ToArray()),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "News fact extraction failed for fixture {FixtureId}, team {TeamId}, document {DocumentId}; continuing with the remaining evidence",
                    fixture.FixtureId,
                    team.TeamId,
                    documentId);
                continue;
            }
            counters.InputTokens += extraction.InputTokens;
            counters.OutputTokens += extraction.OutputTokens;
            if (extraction.FixtureRelevance < _options.MinFixtureRelevance)
                continue;
            var facts = new List<FootballNewsFact>();
            foreach (var extractedFact in extraction.Facts)
            {
                var resolution = await _entityResolver.ResolveAsync(
                    fixture.FixtureId,
                    extractedFact.TeamName,
                    extractedFact.PlayerName,
                    cancellationToken);
                var recency = RecencyFactor(article.PublishedAtUtc ?? firstSeenAtUtc, cutoff);
                var effective = Math.Clamp(
                    source.ConfidenceWeight * extractedFact.ExtractionConfidence
                    * extraction.FixtureRelevance * recency,
                    0m,
                    1m);
                if (effective < _options.MinFactConfidence)
                    continue;
                var position = squad.FirstOrDefault(value => value.PlayerId == resolution.PlayerId)?.Position;
                facts.Add(CreateFact(
                    documentId,
                    fixture.FixtureId,
                    resolution.TeamId,
                    resolution.PlayerId,
                    extractedFact.TeamName,
                    extractedFact.PlayerName,
                    position,
                    extractedFact.EventType,
                    extractedFact.AvailabilityStatus,
                    extractedFact.Certainty,
                    extractedFact.ProbabilityAvailable,
                    extractedFact.Evidence,
                    firstSeenAtUtc,
                    effective,
                    resolution.Status,
                    extraction.ExtractionModel,
                    extraction.PromptVersion,
                    extractedFact.Reason,
                    extractedFact.ExpectedReturnAtUtc,
                    extractedFact.ExpectedMinutesDelta,
                    extraction.FixtureRelevance,
                    extractedFact.ExtractionConfidence,
                    source.ConfidenceWeight));
            }
            if (facts.Count > 0)
            {
                await _factRepository.InsertAsync(facts, cancellationToken);
                counters.Facts += facts.Count;
            }
        }
    }

    private async Task<FootballSourceConfiguration> ResolveSourceAsync(
        string domain,
        CancellationToken cancellationToken)
    {
        var configured = await _sourceRepository.GetAsync(domain, cancellationToken);
        if (configured is not null)
            return configured;
        var defaultWeight = _options.Sources.TryGetValue(nameof(NewsSourceTier.Unknown), out var value)
            ? value
            : 0.45m;
        return new FootballSourceConfiguration(domain, NewsSourceTier.Unknown, defaultWeight, true, DateTime.UtcNow);
    }

    private decimal RecencyFactor(DateTime publishedAtUtc, DateTime cutoffUtc)
    {
        var ageHours = Math.Max(0d, (cutoffUtc - EnsureUtc(publishedAtUtc)).TotalHours);
        return Convert.ToDecimal(Math.Pow(0.5d, ageHours / Math.Max(1, _options.NewsRecencyHalfLifeHours)));
    }

    private static FootballNewsFact CreateFact(
        long documentId,
        long fixtureId,
        int? teamId,
        int? playerId,
        string teamName,
        string? playerName,
        string? position,
        FootballNewsEventType eventType,
        AvailabilityStatus status,
        FactCertainty certainty,
        decimal? probabilityAvailable,
        string evidence,
        DateTime observedAtUtc,
        decimal effectiveConfidence,
        EntityResolutionStatus resolutionStatus,
        string extractionModel,
        string promptVersion = "structured-provider-v1",
        string? reason = null,
        DateTime? expectedReturnAtUtc = null,
        decimal? expectedMinutesDelta = null,
        decimal fixtureRelevance = 1m,
        decimal extractionConfidence = 1m,
        decimal sourceConfidence = 0.95m)
    {
        var factHash = FootballIntelligenceHash.Sha256(string.Join('\u001F',
            fixtureId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            teamId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            playerId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            eventType,
            status,
            certainty,
            probabilityAvailable?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            evidence.Trim(),
            extractionModel,
            promptVersion));
        return new FootballNewsFact
        {
            NewsDocumentId = documentId,
            FactHash = factHash,
            FixtureId = fixtureId,
            TeamId = teamId,
            PlayerId = playerId,
            TeamNameExtracted = teamName,
            PlayerNameExtracted = playerName,
            PositionCode = position,
            EventType = eventType,
            AvailabilityStatus = status,
            Certainty = certainty,
            ProbabilityAvailable = probabilityAvailable,
            ExpectedMinutesDelta = expectedMinutesDelta,
            Reason = reason,
            EvidenceSnippet = evidence,
            EventEffectiveAtUtc = observedAtUtc,
            ExpectedReturnAtUtc = expectedReturnAtUtc,
            FixtureRelevance = fixtureRelevance,
            ExtractionConfidence = extractionConfidence,
            SourceConfidence = sourceConfidence,
            EffectiveConfidence = effectiveConfidence,
            ResolutionStatus = resolutionStatus,
            IsCurrent = true,
            ExtractionModel = extractionModel,
            PromptVersion = promptVersion,
            IsCurrentExtraction = true,
            FirstSeenAtUtc = observedAtUtc,
            CreatedAtUtc = observedAtUtc
        };
    }

    private async Task CompleteRunAsync(MatchIntelligenceRun run, CancellationToken cancellationToken)
    {
        try
        {
            await _runRepository.CompleteAsync(run, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not persist completion for intelligence RunId={RunId}", run.Id);
            if (run.Status != MatchIntelligenceRunStatus.Failed)
                throw;
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed class RunCounters
    {
        public int Queries { get; set; }
        public int SearchResults { get; set; }
        public int DocumentsDownloaded { get; set; }
        public int Facts { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }
}
