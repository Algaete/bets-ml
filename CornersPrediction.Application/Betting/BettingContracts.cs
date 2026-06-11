using CornersPrediction.Domain.Betting;

namespace CornersPrediction.Application.Betting;

public sealed record BettingRecordDto(
    long Id,
    string UserId,
    string CurrencyCode,
    string League,
    string Season,
    DateTime MatchDate,
    string HomeTeam,
    string AwayTeam,
    string? Bookmaker,
    string MarketType,
    string BetSelection,
    decimal Line,
    decimal Odds,
    decimal Stake,
    string Status,
    int? ActualHomeCorners,
    int? ActualAwayCorners,
    int? ActualTotalCorners,
    int? ActualHomeShots,
    int? ActualAwayShots,
    int? ActualTotalShots,
    int? ActualHomeShotsOnGoal,
    int? ActualAwayShotsOnGoal,
    int? ActualTotalShotsOnGoal,
    decimal? CashoutAmount,
    decimal PotentialReturn,
    decimal NetReturn,
    decimal ProfitLoss,
    decimal RoiPercent,
    decimal? BankrollBefore,
    decimal? BankrollAfter,
    decimal? ClosingOdds,
    string? ConfidenceLevel,
    string PredictionModel,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CreateBettingRecordRequest(
    string? UserId,
    string? CurrencyCode,
    string League,
    string Season,
    DateTime MatchDate,
    string HomeTeam,
    string AwayTeam,
    string? Bookmaker,
    string MarketType,
    string BetSelection,
    decimal Line,
    decimal Odds,
    decimal Stake,
    string Status,
    decimal? BankrollBefore,
    decimal? ClosingOdds,
    string? ConfidenceLevel,
    string? PredictionModel,
    string? Notes);

public sealed record UpdateBettingRecordRequest(
    string? UserId,
    string? CurrencyCode,
    string League,
    string Season,
    DateTime MatchDate,
    string HomeTeam,
    string AwayTeam,
    string? Bookmaker,
    string MarketType,
    string BetSelection,
    decimal Line,
    decimal Odds,
    decimal Stake,
    string Status,
    int? ActualHomeCorners,
    int? ActualAwayCorners,
    int? ActualTotalCorners,
    int? ActualHomeShots,
    int? ActualAwayShots,
    int? ActualTotalShots,
    int? ActualHomeShotsOnGoal,
    int? ActualAwayShotsOnGoal,
    int? ActualTotalShotsOnGoal,
    decimal? CashoutAmount,
    decimal? BankrollBefore,
    decimal? ClosingOdds,
    string? ConfidenceLevel,
    string? PredictionModel,
    string? Notes,
    bool AutoResolveStatus = false);

public sealed record BettingFiltersRequest(
    string? UserId,
    string? CurrencyCode,
    string? League,
    string? Season,
    string? HomeTeam,
    string? AwayTeam,
    string? Status,
    string? MarketType,
    string? Bookmaker,
    DateTime? DateFrom,
    DateTime? DateTo);

public sealed record BettingSummaryDto(
    int TotalBets,
    int PendingBets,
    int WonBets,
    int LostBets,
    int VoidBets,
    int CashoutBets,
    decimal TotalStake,
    decimal TotalPotentialReturn,
    decimal TotalNetReturn,
    decimal TotalProfitLoss,
    decimal RoiPercent,
    decimal WinRatePercent,
    decimal AverageOdds,
    decimal AverageStake,
    decimal BestProfit,
    decimal WorstLoss);

public sealed record BankrollTransactionDto(
    long Id,
    string UserId,
    string CurrencyCode,
    DateTime TransactionDate,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    long? BettingRecordId,
    string? Notes,
    DateTime CreatedAt);

public sealed record CreateBankrollTransactionRequest(
    string? UserId,
    string? CurrencyCode,
    DateTime TransactionDate,
    string Type,
    decimal Amount,
    long? BettingRecordId,
    string? Notes);

public interface IBettingRepository
{
    Task<BettingRecord> AddAsync(BettingRecord record, CancellationToken cancellationToken);
    Task<int> UpdateAsync(long id, BettingRecord record, CancellationToken cancellationToken);
    Task<int> DeleteAsync(long id, string userId, CancellationToken cancellationToken);
    Task<BettingRecord?> GetByIdAsync(long id, string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BettingRecord>> GetAsync(BettingFiltersRequest filters, CancellationToken cancellationToken);
    Task<BettingSummaryDto> GetSummaryAsync(BettingFiltersRequest filters, CancellationToken cancellationToken);
    Task<BankrollTransaction> AddBankrollTransactionAsync(BankrollTransaction transaction, CancellationToken cancellationToken);
    Task<BankrollTransaction?> ReconcileBetSettlementAsync(BettingRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<BankrollTransaction>> GetBankrollTransactionsAsync(string userId, string currencyCode, CancellationToken cancellationToken);
    Task<decimal> GetCurrentBankrollAsync(string userId, string currencyCode, CancellationToken cancellationToken);
}

public interface ICreateBettingRecordUseCase
{
    Task<BettingRecordDto> CreateAsync(CreateBettingRecordRequest request, CancellationToken cancellationToken);
}

public interface IUpdateBettingRecordUseCase
{
    Task<int> UpdateAsync(long id, UpdateBettingRecordRequest request, CancellationToken cancellationToken);
}

public interface IDeleteBettingRecordUseCase
{
    Task<int> DeleteAsync(long id, string? userId, CancellationToken cancellationToken);
}

public interface IGetBettingRecordByIdUseCase
{
    Task<BettingRecordDto?> GetAsync(long id, string? userId, CancellationToken cancellationToken);
}

public interface IGetBettingRecordsUseCase
{
    Task<IReadOnlyList<BettingRecordDto>> GetAsync(BettingFiltersRequest filters, CancellationToken cancellationToken);
}

public interface IGetBettingSummaryUseCase
{
    Task<BettingSummaryDto> GetAsync(BettingFiltersRequest filters, CancellationToken cancellationToken);
}

public interface ICreateBankrollTransactionUseCase
{
    Task<BankrollTransactionDto> CreateAsync(CreateBankrollTransactionRequest request, CancellationToken cancellationToken);
}

public interface IGetBankrollTransactionsUseCase
{
    Task<IReadOnlyList<BankrollTransactionDto>> GetAsync(string? userId, string? currencyCode, CancellationToken cancellationToken);
}

public interface IGetCurrentBankrollUseCase
{
    Task<decimal> GetAsync(string? userId, string? currencyCode, CancellationToken cancellationToken);
}

public sealed class CreateBettingRecordUseCase : ICreateBettingRecordUseCase
{
    private readonly IBettingRepository _repository;

    public CreateBettingRecordUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public async Task<BettingRecordDto> CreateAsync(CreateBettingRecordRequest request, CancellationToken cancellationToken)
    {
        BettingValidation.ValidateCreate(request);
        var userId = BettingValidation.NormalizeUserId(request.UserId);
        var currencyCode = BettingValidation.NormalizeCurrency(request.CurrencyCode);

        var record = new BettingRecord
        {
            UserId = userId,
            CurrencyCode = currencyCode,
            League = request.League.Trim(),
            Season = request.Season.Trim(),
            MatchDate = request.MatchDate.Date,
            HomeTeam = request.HomeTeam.Trim(),
            AwayTeam = request.AwayTeam.Trim(),
            Bookmaker = BettingValidation.NormalizeOptional(request.Bookmaker),
            MarketType = BettingValidation.NormalizeRequiredOption(request.MarketType, BetMarketTypes.All, nameof(request.MarketType)),
            BetSelection = BettingValidation.NormalizeRequiredOption(request.BetSelection, BetSelections.All, nameof(request.BetSelection)),
            Line = request.Line,
            Odds = request.Odds,
            Stake = request.Stake,
            Status = BettingValidation.NormalizeRequiredOption(request.Status, BetStatuses.All, nameof(request.Status)),
            BankrollBefore = request.BankrollBefore ?? await _repository.GetCurrentBankrollAsync(userId, currencyCode, cancellationToken),
            ClosingOdds = request.ClosingOdds,
            ConfidenceLevel = BettingValidation.NormalizeOptionalOption(request.ConfidenceLevel, BetConfidenceLevels.All, nameof(request.ConfidenceLevel)),
            PredictionModel = BettingValidation.NormalizeOptionalOption(request.PredictionModel, BetPredictionModels.All, nameof(request.PredictionModel)) ?? BetPredictionModels.Manual,
            Notes = BettingValidation.NormalizeOptional(request.Notes),
            CreatedAt = DateTime.UtcNow
        };

        record.CalculateFinancials();
        var saved = await _repository.AddAsync(record, cancellationToken);
        await _repository.ReconcileBetSettlementAsync(saved, cancellationToken);
        return BettingMapper.ToDto(saved);
    }
}

public sealed class UpdateBettingRecordUseCase : IUpdateBettingRecordUseCase
{
    private readonly IBettingRepository _repository;

    public UpdateBettingRecordUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> UpdateAsync(long id, UpdateBettingRecordRequest request, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Betting record id must be greater than zero.");
        }

        BettingValidation.ValidateUpdate(request);
        var userId = BettingValidation.NormalizeUserId(request.UserId);
        var currencyCode = BettingValidation.NormalizeCurrency(request.CurrencyCode);

        var record = new BettingRecord
        {
            Id = id,
            UserId = userId,
            CurrencyCode = currencyCode,
            League = request.League.Trim(),
            Season = request.Season.Trim(),
            MatchDate = request.MatchDate.Date,
            HomeTeam = request.HomeTeam.Trim(),
            AwayTeam = request.AwayTeam.Trim(),
            Bookmaker = BettingValidation.NormalizeOptional(request.Bookmaker),
            MarketType = BettingValidation.NormalizeRequiredOption(request.MarketType, BetMarketTypes.All, nameof(request.MarketType)),
            BetSelection = BettingValidation.NormalizeRequiredOption(request.BetSelection, BetSelections.All, nameof(request.BetSelection)),
            Line = request.Line,
            Odds = request.Odds,
            Stake = request.Stake,
            Status = BettingValidation.NormalizeRequiredOption(request.Status, BetStatuses.All, nameof(request.Status)),
            ActualHomeCorners = request.ActualHomeCorners,
            ActualAwayCorners = request.ActualAwayCorners,
            ActualTotalCorners = request.ActualTotalCorners,
            ActualHomeShots = request.ActualHomeShots,
            ActualAwayShots = request.ActualAwayShots,
            ActualTotalShots = request.ActualTotalShots,
            ActualHomeShotsOnGoal = request.ActualHomeShotsOnGoal,
            ActualAwayShotsOnGoal = request.ActualAwayShotsOnGoal,
            ActualTotalShotsOnGoal = request.ActualTotalShotsOnGoal,
            CashoutAmount = request.CashoutAmount,
            BankrollBefore = request.BankrollBefore,
            ClosingOdds = request.ClosingOdds,
            ConfidenceLevel = BettingValidation.NormalizeOptionalOption(request.ConfidenceLevel, BetConfidenceLevels.All, nameof(request.ConfidenceLevel)),
            PredictionModel = BettingValidation.NormalizeOptionalOption(request.PredictionModel, BetPredictionModels.All, nameof(request.PredictionModel)) ?? BetPredictionModels.Manual,
            Notes = BettingValidation.NormalizeOptional(request.Notes),
            UpdatedAt = DateTime.UtcNow
        };

        record.CalculateFinancials(request.AutoResolveStatus);
        var rowsAffected = await _repository.UpdateAsync(id, record, cancellationToken);
        if (rowsAffected > 0)
        {
            await _repository.ReconcileBetSettlementAsync(record, cancellationToken);
        }

        return rowsAffected;
    }
}

public sealed class DeleteBettingRecordUseCase : IDeleteBettingRecordUseCase
{
    private readonly IBettingRepository _repository;

    public DeleteBettingRecordUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public Task<int> DeleteAsync(long id, string? userId, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Betting record id must be greater than zero.");
        }

        return DeleteAndReverseSettlementAsync(id, BettingValidation.NormalizeUserId(userId), cancellationToken);
    }

    private async Task<int> DeleteAndReverseSettlementAsync(long id, string userId, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByIdAsync(id, userId, cancellationToken);
        if (existing is not null)
        {
            existing.Status = BetStatuses.Pending;
            existing.ProfitLoss = 0;
            existing.NetReturn = 0;
            existing.RoiPercent = 0;
            await _repository.ReconcileBetSettlementAsync(existing, cancellationToken);
        }

        return await _repository.DeleteAsync(id, userId, cancellationToken);
    }
}

public sealed class GetBettingRecordByIdUseCase : IGetBettingRecordByIdUseCase
{
    private readonly IBettingRepository _repository;

    public GetBettingRecordByIdUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public async Task<BettingRecordDto?> GetAsync(long id, string? userId, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Betting record id must be greater than zero.");
        }

        var record = await _repository.GetByIdAsync(id, BettingValidation.NormalizeUserId(userId), cancellationToken);
        return record is null ? null : BettingMapper.ToDto(record);
    }
}

public sealed class GetBettingRecordsUseCase : IGetBettingRecordsUseCase
{
    private readonly IBettingRepository _repository;

    public GetBettingRecordsUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<BettingRecordDto>> GetAsync(BettingFiltersRequest filters, CancellationToken cancellationToken)
    {
        var records = await _repository.GetAsync(BettingValidation.NormalizeFilters(filters), cancellationToken);
        return records.Select(BettingMapper.ToDto).ToArray();
    }
}

public sealed class GetBettingSummaryUseCase : IGetBettingSummaryUseCase
{
    private readonly IBettingRepository _repository;

    public GetBettingSummaryUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public Task<BettingSummaryDto> GetAsync(BettingFiltersRequest filters, CancellationToken cancellationToken)
    {
        return _repository.GetSummaryAsync(BettingValidation.NormalizeFilters(filters), cancellationToken);
    }
}

public sealed class CreateBankrollTransactionUseCase : ICreateBankrollTransactionUseCase
{
    private readonly IBettingRepository _repository;

    public CreateBankrollTransactionUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public async Task<BankrollTransactionDto> CreateAsync(CreateBankrollTransactionRequest request, CancellationToken cancellationToken)
    {
        var currencyCode = BettingValidation.NormalizeCurrency(request.CurrencyCode);
        var userId = BettingValidation.NormalizeUserId(request.UserId);
        var type = BettingValidation.NormalizeRequiredOption(request.Type, BankrollTransactionTypes.All, nameof(request.Type));
        var amount = NormalizeBankrollAmount(type, request.Amount);

        if (request.TransactionDate == default)
        {
            throw new ArgumentException("Transaction date is required.");
        }

        if (amount == 0)
        {
            throw new ArgumentException("Transaction amount cannot be zero.");
        }

        if (request.BettingRecordId is <= 0)
        {
            throw new ArgumentException("Betting record id must be greater than zero when provided.");
        }

        var transaction = new BankrollTransaction
        {
            UserId = userId,
            CurrencyCode = currencyCode,
            TransactionDate = request.TransactionDate.Date,
            Type = type,
            Amount = amount,
            BettingRecordId = request.BettingRecordId,
            Notes = BettingValidation.NormalizeOptional(request.Notes),
            CreatedAt = DateTime.UtcNow
        };

        var saved = await _repository.AddBankrollTransactionAsync(transaction, cancellationToken);
        return BettingMapper.ToDto(saved);
    }

    private static decimal NormalizeBankrollAmount(string type, decimal amount)
    {
        return type switch
        {
            BankrollTransactionTypes.Deposit => Math.Abs(amount),
            BankrollTransactionTypes.Withdrawal => -Math.Abs(amount),
            _ => amount
        };
    }
}

public sealed class GetBankrollTransactionsUseCase : IGetBankrollTransactionsUseCase
{
    private readonly IBettingRepository _repository;

    public GetBankrollTransactionsUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<BankrollTransactionDto>> GetAsync(string? userId, string? currencyCode, CancellationToken cancellationToken)
    {
        var transactions = await _repository.GetBankrollTransactionsAsync(
            BettingValidation.NormalizeUserId(userId),
            BettingValidation.NormalizeCurrency(currencyCode),
            cancellationToken);
        return transactions.Select(BettingMapper.ToDto).ToArray();
    }
}

public sealed class GetCurrentBankrollUseCase : IGetCurrentBankrollUseCase
{
    private readonly IBettingRepository _repository;

    public GetCurrentBankrollUseCase(IBettingRepository repository)
    {
        _repository = repository;
    }

    public Task<decimal> GetAsync(string? userId, string? currencyCode, CancellationToken cancellationToken)
    {
        return _repository.GetCurrentBankrollAsync(
            BettingValidation.NormalizeUserId(userId),
            BettingValidation.NormalizeCurrency(currencyCode),
            cancellationToken);
    }
}

internal static class BettingMapper
{
    public static BettingRecordDto ToDto(BettingRecord record)
    {
        return new BettingRecordDto(
            record.Id,
            record.UserId,
            record.CurrencyCode,
            record.League,
            record.Season,
            record.MatchDate,
            record.HomeTeam,
            record.AwayTeam,
            record.Bookmaker,
            record.MarketType,
            record.BetSelection,
            record.Line,
            record.Odds,
            record.Stake,
            record.Status,
            record.ActualHomeCorners,
            record.ActualAwayCorners,
            record.ActualTotalCorners,
            record.ActualHomeShots,
            record.ActualAwayShots,
            record.ActualTotalShots,
            record.ActualHomeShotsOnGoal,
            record.ActualAwayShotsOnGoal,
            record.ActualTotalShotsOnGoal,
            record.CashoutAmount,
            record.PotentialReturn,
            record.NetReturn,
            record.ProfitLoss,
            record.RoiPercent,
            record.BankrollBefore,
            record.BankrollAfter,
            record.ClosingOdds,
            record.ConfidenceLevel,
            record.PredictionModel,
            record.Notes,
            record.CreatedAt,
            record.UpdatedAt);
    }

    public static BankrollTransactionDto ToDto(BankrollTransaction transaction)
    {
        return new BankrollTransactionDto(
            transaction.Id,
            transaction.UserId,
            transaction.CurrencyCode,
            transaction.TransactionDate,
            transaction.Type,
            transaction.Amount,
            transaction.BalanceAfter,
            transaction.BettingRecordId,
            transaction.Notes,
            transaction.CreatedAt);
    }
}

internal static class BettingValidation
{
    public static void ValidateCreate(CreateBettingRecordRequest request)
    {
        ValidateBase(
            request.League,
            request.Season,
            request.MatchDate,
            request.HomeTeam,
            request.AwayTeam,
            request.MarketType,
            request.BetSelection,
            request.Line,
            request.Odds,
            request.Stake,
            request.Status,
            cashoutAmount: null,
            request.ClosingOdds,
            request.ConfidenceLevel,
            request.PredictionModel);
    }

    public static void ValidateUpdate(UpdateBettingRecordRequest request)
    {
        ValidateBase(
            request.League,
            request.Season,
            request.MatchDate,
            request.HomeTeam,
            request.AwayTeam,
            request.MarketType,
            request.BetSelection,
            request.Line,
            request.Odds,
            request.Stake,
            request.Status,
            request.CashoutAmount,
            request.ClosingOdds,
            request.ConfidenceLevel,
            request.PredictionModel);

        if (request.ActualHomeCorners is < 0 ||
            request.ActualAwayCorners is < 0 ||
            request.ActualTotalCorners is < 0 ||
            request.ActualHomeShots is < 0 ||
            request.ActualAwayShots is < 0 ||
            request.ActualTotalShots is < 0 ||
            request.ActualHomeShotsOnGoal is < 0 ||
            request.ActualAwayShotsOnGoal is < 0 ||
            request.ActualTotalShotsOnGoal is < 0)
        {
            throw new ArgumentException("Actual match stat values cannot be negative.");
        }
    }

    public static BettingFiltersRequest NormalizeFilters(BettingFiltersRequest filters)
    {
        return filters with
        {
            CurrencyCode = NormalizeOptionalCurrency(filters.CurrencyCode),
            UserId = NormalizeUserId(filters.UserId),
            League = NormalizeOptional(filters.League),
            Season = NormalizeOptional(filters.Season),
            HomeTeam = NormalizeOptional(filters.HomeTeam),
            AwayTeam = NormalizeOptional(filters.AwayTeam),
            Status = NormalizeOptional(filters.Status),
            MarketType = NormalizeOptional(filters.MarketType),
            Bookmaker = NormalizeOptional(filters.Bookmaker),
            DateFrom = filters.DateFrom?.Date,
            DateTo = filters.DateTo?.Date
        };
    }

    public static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string NormalizeUserId(string? value)
    {
        return NormalizeOptional(value) ?? BettingUsers.DefaultUserId;
    }

    public static string NormalizeCurrency(string? value)
    {
        var normalized = NormalizeOptional(value) ?? BettingCurrencies.Clp;
        return BettingCurrencies.All.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException("CurrencyCode has an unsupported value.");
    }

    public static string? NormalizeOptionalCurrency(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        return BettingCurrencies.All.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException("CurrencyCode has an unsupported value.");
    }

    public static string NormalizeRequiredOption(string value, IReadOnlyCollection<string> allowedValues, string fieldName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            throw new ArgumentException($"{fieldName} is required.");
        }

        return allowedValues.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException($"{fieldName} has an unsupported value.");
    }

    public static string? NormalizeOptionalOption(string? value, IReadOnlyCollection<string> allowedValues, string fieldName)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        return allowedValues.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException($"{fieldName} has an unsupported value.");
    }

    private static void ValidateBase(
        string league,
        string season,
        DateTime matchDate,
        string homeTeam,
        string awayTeam,
        string marketType,
        string betSelection,
        decimal line,
        decimal odds,
        decimal stake,
        string status,
        decimal? cashoutAmount,
        decimal? closingOdds,
        string? confidenceLevel,
        string? predictionModel)
    {
        if (string.IsNullOrWhiteSpace(league))
        {
            throw new ArgumentException("League is required.");
        }

        if (string.IsNullOrWhiteSpace(season))
        {
            throw new ArgumentException("Season is required.");
        }

        if (matchDate == default)
        {
            throw new ArgumentException("Match date is required.");
        }

        if (string.IsNullOrWhiteSpace(homeTeam))
        {
            throw new ArgumentException("Home team is required.");
        }

        if (string.IsNullOrWhiteSpace(awayTeam))
        {
            throw new ArgumentException("Away team is required.");
        }

        if (homeTeam.Trim().Equals(awayTeam.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Home team and away team must be different.");
        }

        NormalizeRequiredOption(marketType, BetMarketTypes.All, nameof(marketType));
        NormalizeRequiredOption(betSelection, BetSelections.All, nameof(betSelection));
        var normalizedStatus = NormalizeRequiredOption(status, BetStatuses.All, nameof(status));
        NormalizeOptionalOption(confidenceLevel, BetConfidenceLevels.All, nameof(confidenceLevel));
        NormalizeOptionalOption(predictionModel, BetPredictionModels.All, nameof(predictionModel));

        if (line < 0)
        {
            throw new ArgumentException("Line must be greater than or equal to zero.");
        }

        if (odds <= 1)
        {
            throw new ArgumentException("Odds must be greater than 1.");
        }

        if (closingOdds is <= 1)
        {
            throw new ArgumentException("Closing odds must be greater than 1 when provided.");
        }

        if (stake <= 0)
        {
            throw new ArgumentException("Stake must be greater than zero.");
        }

        if (normalizedStatus == BetStatuses.Cashout && cashoutAmount is null)
        {
            throw new ArgumentException("Cashout amount is required when status is Cashout.");
        }

        if (cashoutAmount < 0)
        {
            throw new ArgumentException("Cashout amount cannot be negative.");
        }
    }
}
