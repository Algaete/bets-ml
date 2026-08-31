using System.Text.Json;
using CornersPrediction.Domain.FootballIntelligence;
using Microsoft.Extensions.Options;

namespace CornersPrediction.Application.FootballIntelligence;

public sealed class FootballIntelligenceSnapshotBuilder : IIntelligenceSnapshotBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FootballIntelligenceOptions _options;

    public FootballIntelligenceSnapshotBuilder(IOptions<FootballIntelligenceOptions> options)
    {
        _options = options.Value;
    }

    public MatchTeamIntelligenceSnapshot Build(
        IntelligenceFixtureDto fixture,
        IntelligenceTeamDto team,
        bool isHomeTeam,
        DateTime cutoffAtUtc,
        IReadOnlyCollection<FootballNewsDocument> documents,
        IReadOnlyCollection<FootballNewsFact> facts,
        IReadOnlyCollection<SquadPlayerDto> squad,
        FixtureLineupDto? officialLineup)
    {
        var cutoff = EnsureUtc(cutoffAtUtc);
        var teamDocuments = documents
            .Where(value => value.TeamId == team.TeamId && EnsureUtc(value.FirstSeenAtUtc) <= cutoff)
            .Where(value => !value.PublishedAtUtc.HasValue || EnsureUtc(value.PublishedAtUtc.Value) <= cutoff)
            .ToArray();
        var teamFacts = facts
            .Where(value => value.TeamId == team.TeamId && EnsureUtc(value.FirstSeenAtUtc) <= cutoff)
            .ToArray();
        var currentFacts = teamFacts.Where(value => value.IsCurrent).ToArray();
        var actionable = currentFacts.Where(IsNegativeAvailability).ToArray();
        var actionableDocumentIds = actionable.Select(value => value.NewsDocumentId).ToHashSet();
        var playerById = squad.ToDictionary(value => value.PlayerId);
        var impacts = actionable.Select(fact => BuildImpact(fact, playerById)).ToArray();

        var attackImpact = Sum(impacts.Where(value => value.Group == PositionGroup.Attack));
        var midfieldImpact = Sum(impacts.Where(value => value.Group == PositionGroup.Midfield));
        var defenceImpact = Sum(impacts.Where(value => value.Group == PositionGroup.Defence));
        var goalkeeperImpact = Sum(impacts.Where(value => value.Group == PositionGroup.Goalkeeper));
        var widthImpact = Sum(impacts.Where(value => value.IsWinger || value.IsFullBack));
        var setPieceImpact = Sum(impacts.Where(value => value.IsSetPiece));
        var confidence = actionable.Length == 0
            ? 0m
            : Math.Clamp(actionable.Average(value => value.EffectiveConfidence), 0m, 1m);
        var conflicts = teamFacts
            .Where(value => value.PlayerId.HasValue || !string.IsNullOrWhiteSpace(value.PlayerNameExtracted))
            .GroupBy(value => value.PlayerId?.ToString() ?? value.PlayerNameExtracted, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Select(value => AvailabilityPolarity(value.AvailabilityStatus)).Distinct().Count() > 1);
        var latestEvidence = teamDocuments
            .Select(value => value.PublishedAtUtc ?? value.FirstSeenAtUtc)
            .Concat(teamFacts.Select(value => value.EventEffectiveAtUtc ?? value.FirstSeenAtUtc))
            .Where(value => EnsureUtc(value) <= cutoff)
            .Select(EnsureUtc)
            .DefaultIfEmpty(cutoff)
            .Max();
        var ageMinutes = Math.Max(0, checked((int)Math.Min(int.MaxValue, (cutoff - latestEvidence).TotalMinutes)));
        var rotationRisk = RiskFromFacts(currentFacts, FootballNewsEventType.Rotation, FootballNewsEventType.Rest);
        var fatigueRisk = RiskFromFacts(currentFacts, FootballNewsEventType.Fatigue, FootballNewsEventType.TravelIssue);
        var morale = Math.Clamp(
            currentFacts.Where(value => value.EventType == FootballNewsEventType.MoralePositive).Sum(value => value.EffectiveConfidence)
            - currentFacts.Where(value => value.EventType == FootballNewsEventType.MoraleNegative).Sum(value => value.EffectiveConfidence),
            -1m,
            1m);
        var officialPlayers = officialLineup?.Players ?? [];
        var riskFlags = new List<string>();
        if (impacts.Any(value => value.UsesFallbackImportance))
            riskFlags.Add("FallbackPlayerImportance");
        if (actionable.Length == 0)
            riskFlags.Add("NoActionableFacts");
        if (conflicts > 0)
            riskFlags.Add("ConflictingFacts");

        return new MatchTeamIntelligenceSnapshot
        {
            FixtureId = fixture.FixtureId,
            TeamId = team.TeamId,
            IsHomeTeam = isHomeTeam,
            CutoffAtUtc = cutoff,
            KickoffAtUtc = EnsureUtc(fixture.KickoffUtc),
            DocumentCount = teamDocuments.Length,
            IndependentSourceCount = teamDocuments
                .Where(value => actionableDocumentIds.Contains(value.Id))
                .Select(value => value.SourceDomain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            ActionableFactCount = actionable.Length,
            StructuredEvidenceCount = teamDocuments.Count(value => value.SourceTier == NewsSourceTier.StructuredProvider),
            ConfirmedOutCount = actionable.Count(value => value.AvailabilityStatus is AvailabilityStatus.ConfirmedOut or AvailabilityStatus.NotCalled),
            DoubtfulCount = actionable.Count(value => value.AvailabilityStatus == AvailabilityStatus.Doubtful),
            SuspendedCount = actionable.Count(value => value.AvailabilityStatus == AvailabilityStatus.Suspended),
            MissingStarterMinutesPct = Math.Clamp(Sum(impacts), 0m, 1m),
            MissingAttackMinutesPct = attackImpact,
            MissingMidfieldMinutesPct = midfieldImpact,
            MissingDefenceMinutesPct = defenceImpact,
            AttackAvailabilityImpact = attackImpact,
            MidfieldAvailabilityImpact = midfieldImpact,
            DefenceAvailabilityImpact = defenceImpact,
            GoalkeeperAvailabilityImpact = goalkeeperImpact,
            WidthAvailabilityImpact = widthImpact,
            SetPieceAvailabilityImpact = setPieceImpact,
            RotationRisk = rotationRisk,
            FatigueRisk = fatigueRisk,
            MoraleSignal = morale,
            CoachChangeDays = null,
            ExpectedFormation = officialLineup?.Formation,
            FormationChangeExpected = currentFacts.Any(value => value.EventType == FootballNewsEventType.FormationChange),
            OfficialLineupAvailable = officialPlayers.Count > 0,
            ExpectedXiChanges = actionable.Count(value => value.PlayerId.HasValue && officialPlayers.All(player => player.PlayerId != value.PlayerId)),
            OverallNewsConfidence = confidence,
            ConflictCount = conflicts,
            SnapshotAgeMinutes = ageMinutes,
            MissingWingerMinutesPct = Sum(impacts.Where(value => value.IsWinger)),
            MissingFullBackMinutesPct = Sum(impacts.Where(value => value.IsFullBack)),
            MissingCornerTakerShare = setPieceImpact,
            MissingCrossShare = widthImpact,
            CornerCreationImpact = Math.Clamp((attackImpact + widthImpact + setPieceImpact) / 3m, 0m, 1m),
            MissingShotShare = attackImpact,
            MissingCreatorShare = Math.Clamp((attackImpact + midfieldImpact) / 2m, 0m, 1m),
            ShotGenerationImpact = Math.Clamp((attackImpact + midfieldImpact) / 2m, 0m, 1m),
            MissingSotShare = attackImpact,
            FinishingAvailabilityImpact = attackImpact,
            MissingGoalShare = attackImpact,
            PenaltyTakerMissing = actionable.Any(value =>
                value.EventType == FootballNewsEventType.SetPieceChange
                && (value.Reason?.Contains("penalty", StringComparison.OrdinalIgnoreCase) == true
                    || value.Reason?.Contains("penal", StringComparison.OrdinalIgnoreCase) == true)),
            GoalScoringAvailabilityImpact = attackImpact,
            RiskFlagsJson = JsonSerializer.Serialize(riskFlags, JsonOptions),
            DetailJson = JsonSerializer.Serialize(new
            {
                team = new { team.TeamId, team.Name, isHomeTeam },
                evidence = new { documents = teamDocuments.Length, facts = teamFacts.Length, actionable = actionable.Length, conflicts },
                missingPlayers = impacts.Select(value => new
                {
                    value.PlayerId,
                    value.PlayerName,
                    value.Position,
                    value.Group,
                    value.Impact,
                    value.UsesFallbackImportance
                }),
                formula = "importance * unavailability * effectiveConfidence * replacementGap",
                generatedAtUtc = DateTime.UtcNow
            }, JsonOptions),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private PlayerImpact BuildImpact(
        FootballNewsFact fact,
        IReadOnlyDictionary<int, SquadPlayerDto> playerById)
    {
        playerById.TryGetValue(fact.PlayerId ?? 0, out var squadPlayer);
        var position = fact.PositionCode ?? squadPlayer?.Position;
        var group = Position(position);
        var fallbackImportance = group switch
        {
            PositionGroup.Goalkeeper => _options.SnapshotImpact.GoalkeeperFallbackImportance,
            PositionGroup.Defence => _options.SnapshotImpact.DefenderFallbackImportance,
            PositionGroup.Midfield => _options.SnapshotImpact.MidfielderFallbackImportance,
            PositionGroup.Attack => _options.SnapshotImpact.AttackerFallbackImportance,
            _ => _options.SnapshotImpact.UnknownPositionFallbackImportance
        };
        var unavailable = fact.ProbabilityAvailable.HasValue
            ? 1m - Math.Clamp(fact.ProbabilityAvailable.Value, 0m, 1m)
            : fact.AvailabilityStatus switch
            {
                AvailabilityStatus.Doubtful => _options.SnapshotImpact.DoubtfulUnavailability,
                AvailabilityStatus.ExpectedOut => _options.SnapshotImpact.ExpectedOutUnavailability,
                AvailabilityStatus.ConfirmedOut or AvailabilityStatus.Suspended
                    or AvailabilityStatus.Rested or AvailabilityStatus.NotCalled => 1m,
                _ => 0m
            };
        var impact = Math.Clamp(
            fallbackImportance * unavailable * fact.EffectiveConfidence * _options.SnapshotImpact.UnknownReplacementGap,
            0m,
            _options.SnapshotImpact.MaximumPlayerImpact);
        var normalizedPosition = position?.Trim().ToUpperInvariant() ?? string.Empty;
        return new PlayerImpact(
            fact.PlayerId,
            fact.PlayerNameExtracted,
            normalizedPosition,
            group,
            impact,
            group != PositionGroup.Unknown,
            normalizedPosition is "RW" or "LW" or "W" or "RM" or "LM",
            normalizedPosition is "RB" or "LB" or "RWB" or "LWB",
            fact.EventType == FootballNewsEventType.SetPieceChange);
    }

    private static bool IsNegativeAvailability(FootballNewsFact fact) =>
        fact.AvailabilityStatus is AvailabilityStatus.Doubtful
            or AvailabilityStatus.ExpectedOut
            or AvailabilityStatus.ConfirmedOut
            or AvailabilityStatus.Suspended
            or AvailabilityStatus.Rested
            or AvailabilityStatus.NotCalled;

    private static int AvailabilityPolarity(AvailabilityStatus value) => value switch
    {
        AvailabilityStatus.Available or AvailabilityStatus.ExpectedAvailable
            or AvailabilityStatus.Starting or AvailabilityStatus.Bench => 1,
        AvailabilityStatus.Doubtful or AvailabilityStatus.ExpectedOut
            or AvailabilityStatus.ConfirmedOut or AvailabilityStatus.Suspended
            or AvailabilityStatus.Rested or AvailabilityStatus.NotCalled => -1,
        _ => 0
    };

    private static PositionGroup Position(string? position)
    {
        var value = position?.Trim().ToUpperInvariant() ?? string.Empty;
        if (value is "G" or "GK" or "GOALKEEPER") return PositionGroup.Goalkeeper;
        if (value is "D" or "CB" or "LB" or "RB" or "LWB" or "RWB" or "DEFENDER") return PositionGroup.Defence;
        if (value is "M" or "CM" or "DM" or "AM" or "LM" or "RM" or "MIDFIELDER") return PositionGroup.Midfield;
        if (value is "F" or "ST" or "CF" or "LW" or "RW" or "W" or "ATTACKER") return PositionGroup.Attack;
        return PositionGroup.Unknown;
    }

    private static decimal RiskFromFacts(
        IEnumerable<FootballNewsFact> facts,
        params FootballNewsEventType[] types) =>
        Math.Clamp(facts.Where(value => types.Contains(value.EventType)).Sum(value => value.EffectiveConfidence), 0m, 1m);

    private static decimal Sum(IEnumerable<PlayerImpact> values) =>
        Math.Clamp(values.Sum(value => value.Impact), 0m, 1m);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private enum PositionGroup { Unknown, Goalkeeper, Defence, Midfield, Attack }

    private sealed record PlayerImpact(
        int? PlayerId,
        string? PlayerName,
        string Position,
        PositionGroup Group,
        decimal Impact,
        bool UsesFallbackImportance,
        bool IsWinger,
        bool IsFullBack,
        bool IsSetPiece);
}
