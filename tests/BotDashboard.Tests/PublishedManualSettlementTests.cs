using CornersPrediction.Application.AutomatedCorners;
using CornersPrediction.Infrastructure.SqlServer;
using Microsoft.Extensions.Configuration;

internal static class PublishedManualSettlementTests
{
    public static async Task RunAsync()
    {
        var shared = new CapturingSharedSettlements();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // These tests stop inside the shared service, before any SQL is opened.
            ["ConnectionStrings:DefaultConnection"] = "Server=not-used;Database=not-used"
        }).Build();
        IAutomatedCornerSelectionsRepository repository =
            new SqlServerAutomatedCornerSelectionsRepository(configuration, shared);
        IResolveAutomatedCornerSelectionUseCase useCase = new ResolveAutomatedCornerSelectionUseCase(repository);
        using var cancellation = new CancellationTokenSource();

        await ReachesShared(() => useCase.ResolveAsync(3840,
            new ResolveAutomatedCornerSelectionRequest(1), "  operator@example.test  ", cancellation.Token));
        Check(shared.RecordId == -3840, "Published selection must use the shared published-record identity.");
        Check(shared.Request!.ApplyToFixture, "Productive settlement must propagate across every bot for the fixture and market.");
        Check(shared.Request.ActualValue == 1, "The market's actual result must survive the published route.");
        Check(shared.Request.RequestId != Guid.Empty, "The shared audit needs a nonempty operation identifier.");
        Check(!string.IsNullOrWhiteSpace(shared.Request.Reason), "The legacy route must keep an explicit audit reason.");
        Check(shared.Actor == "operator@example.test", "The trusted acting user must be audited, with surrounding whitespace removed.");
        Check(shared.Cancellation == cancellation.Token, "The shared operation must use the request cancellation token.");
        Console.WriteLine("PASS productive settlement reuses shared fixture-and-market settlement with the acting user");

        await ReachesShared(() => useCase.ResolveAsync(3841,
            new ResolveAutomatedCornerSelectionRequest(0), CancellationToken.None));
        Check(shared.Request!.ActualValue == 0, "Zero remains a valid manual statistic.");
        Check(shared.Actor == ResolveAutomatedCornerSelectionUseCase.LegacyManualSettlementActor,
            "Existing callers without an acting user must keep a declared legacy actor.");
        await ReachesShared(() => repository.ResolveAsync(3842, 1, CancellationToken.None));
        Check(shared.RecordId == -3842 && shared.Request!.ApplyToFixture,
            "The original repository overload must also settle every matching bot.");
        Check(shared.Actor == ResolveAutomatedCornerSelectionUseCase.LegacyManualSettlementActor,
            "Existing repository callers must keep a declared legacy actor.");
        Console.WriteLine("PASS legacy productive settlement callers still propagate, including a zero result");

        var calls = shared.Calls;
        await Rejects(() => useCase.ResolveAsync(0, new ResolveAutomatedCornerSelectionRequest(1), "operator", CancellationToken.None));
        await Rejects(() => useCase.ResolveAsync(1, new ResolveAutomatedCornerSelectionRequest(-1), "operator", CancellationToken.None));
        await Rejects(() => useCase.ResolveAsync(1, new ResolveAutomatedCornerSelectionRequest(1001), "operator", CancellationToken.None));
        await Rejects(() => useCase.ResolveAsync(1, new ResolveAutomatedCornerSelectionRequest(1), new string('a', 257), CancellationToken.None));
        await Rejects(() => repository.ResolveAsync(-1, 1, "operator", CancellationToken.None));
        Check(shared.Calls == calls, "Invalid input must never reach the shared settlement service.");
        Console.WriteLine("PASS invalid productive settlement input is rejected before any settlement write");
    }

    private static async Task ReachesShared(Func<Task<AutomatedCornerSelectionDto>> action)
    {
        try { await action(); }
        catch (ReachedSharedSettlementException) { return; }
        throw new InvalidOperationException("The productive route did not reach the shared settlement service.");
    }

    private static async Task Rejects(Func<Task<AutomatedCornerSelectionDto>> action)
    {
        try { await action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("The productive route accepted invalid settlement input.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ReachedSharedSettlementException : Exception { }

    private sealed class CapturingSharedSettlements : IGeneralPickManualSettlementRepository
    {
        public int Calls { get; private set; }
        public long RecordId { get; private set; }
        public GeneralPickManualSettlementRequest? Request { get; private set; }
        public string? Actor { get; private set; }
        public CancellationToken Cancellation { get; private set; }

        public Task<GeneralPickSettlementScope> PreviewAsync(long recordId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GeneralPickManualSettlementResult> SettleAsync(long recordId,
            GeneralPickManualSettlementRequest request, string actor, CancellationToken cancellationToken)
        {
            Calls++;
            RecordId = recordId;
            Request = request;
            Actor = actor;
            Cancellation = cancellationToken;
            return Task.FromException<GeneralPickManualSettlementResult>(new ReachedSharedSettlementException());
        }
    }
}
