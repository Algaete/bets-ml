using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.Betting;
using CornersPrediction.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CornersPrediction.Web.Controllers;

[Authorize]
public sealed class BettingController : Controller
{
    private readonly BettingApiClient _bettingApiClient;
    private readonly ILogger<BettingController> _logger;

    public BettingController(BettingApiClient bettingApiClient, ILogger<BettingController> logger)
    {
        _bettingApiClient = bettingApiClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] BettingFiltersViewModel filters, CancellationToken cancellationToken)
    {
        try
        {
            return View(await BuildIndexViewModelAsync(filters, cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load betting records");
            ModelState.AddModelError(string.Empty, "Betting data could not be loaded. Check that the API and SQL stored procedures are available.");
            return View(new BettingIndexViewModel
            {
                Filters = filters,
                WorkingCurrencyCode = NormalizeOptionalCurrency(filters.CurrencyCode) ?? "CLP"
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Export(
        [FromQuery] string format,
        [FromQuery] BettingFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await BuildIndexViewModelAsync(filters, cancellationToken);
            var generatedAtUtc = DateTime.UtcNow;
            var suffix = generatedAtUtc.ToString("yyyyMMdd-HHmmss");

            if (format.Equals("excel", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = BettingExportBuilder.BuildExcel(model, generatedAtUtc);
                return File(bytes, "application/vnd.ms-excel", $"betting-export-{suffix}.xls");
            }

            if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = BettingExportBuilder.BuildPdf(model, generatedAtUtc);
                return File(bytes, "application/pdf", $"betting-export-{suffix}.pdf");
            }

            return BadRequest("Unsupported export format.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not export betting data");
            TempData["ErrorMessage"] = "Betting data could not be exported.";
            return RedirectToAction(nameof(Index), filters);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Create(
        [FromQuery] string? currencyCode,
        [FromQuery] string? league,
        [FromQuery] string? season,
        [FromQuery] DateTime? matchDate,
        [FromQuery] string? homeTeam,
        [FromQuery] string? awayTeam,
        [FromQuery] string? marketType,
        [FromQuery] string? betSelection,
        [FromQuery] decimal? line,
        [FromQuery] decimal? estimatedProbabilityPercent,
        [FromQuery] string? kellyStrategy,
        [FromQuery] string? confidenceLevel,
        [FromQuery] string? predictionModel,
        [FromQuery] string? notes,
        CancellationToken cancellationToken)
    {
        var selectedCurrency = NormalizeCurrency(currencyCode);
        var currentBankroll = await _bettingApiClient.GetCurrentBankrollAsync(selectedCurrency, cancellationToken);
        return View(new BettingRecordFormViewModel
        {
            CurrencyCode = selectedCurrency,
            League = league?.Trim() ?? string.Empty,
            Season = string.IsNullOrWhiteSpace(season) ? "2025-2026" : season.Trim(),
            MatchDate = matchDate ?? DateTime.Today,
            HomeTeam = homeTeam?.Trim() ?? string.Empty,
            AwayTeam = awayTeam?.Trim() ?? string.Empty,
            MarketType = NormalizeOption(marketType, BettingOptions.MarketTypes, "TotalCorners"),
            BetSelection = NormalizeOption(betSelection, BettingOptions.BetSelections, "Over"),
            Line = line.GetValueOrDefault(),
            EstimatedProbabilityPercent = estimatedProbabilityPercent,
            KellyStrategy = NormalizeOption(kellyStrategy, BettingOptions.KellyStrategies, "None"),
            ConfidenceLevel = NormalizeOption(confidenceLevel, BettingOptions.ConfidenceLevels, string.Empty),
            PredictionModel = NormalizeOption(predictionModel, BettingOptions.PredictionModels, "Manual"),
            Notes = notes,
            BankrollBefore = currentBankroll
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BettingRecordFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        try
        {
            await _bettingApiClient.CreateAsync(form, cancellationToken);
            TempData["SuccessMessage"] = "Betting record saved successfully.";
            return RedirectToAction(nameof(Index), new { currencyCode = form.CurrencyCode });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not create betting record");
            ModelState.AddModelError(string.Empty, $"The betting record could not be saved. {exception.Message}");
            return View(form);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var record = await _bettingApiClient.GetByIdAsync(id, cancellationToken);
        return record is null ? NotFound() : View(record);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var record = await _bettingApiClient.GetByIdAsync(id, cancellationToken);
        return record is null ? NotFound() : View(ToForm(record));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(BettingRecordFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        try
        {
            await _bettingApiClient.UpdateAsync(form, cancellationToken);
            TempData["SuccessMessage"] = "Betting record updated successfully.";
            return RedirectToAction(nameof(Index), new { currencyCode = form.CurrencyCode });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not update betting record {BettingRecordId}", form.Id);
            ModelState.AddModelError(string.Empty, "The betting record could not be updated.");
            return View(form);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id, string? currencyCode, CancellationToken cancellationToken)
    {
        try
        {
            await _bettingApiClient.DeleteAsync(id, cancellationToken);
            TempData["SuccessMessage"] = "Betting record deleted.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not delete betting record {BettingRecordId}", id);
            TempData["ErrorMessage"] = "The betting record could not be deleted.";
        }

        return RedirectToAction(nameof(Index), new { currencyCode = NormalizeCurrency(currencyCode) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkStatus(long id, string status, CancellationToken cancellationToken)
    {
        if (status == "Cashout")
        {
            return RedirectToAction(nameof(Edit), new { id });
        }

        try
        {
            var record = await _bettingApiClient.GetByIdAsync(id, cancellationToken);
            if (record is null)
            {
                return NotFound();
            }

            var form = ToForm(record);
            form.Status = status;
            await _bettingApiClient.UpdateAsync(form, cancellationToken);
            TempData["SuccessMessage"] = $"Betting record marked as {status}.";
            return RedirectToAction(nameof(Index), new { currencyCode = form.CurrencyCode });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not mark betting record {BettingRecordId} as {Status}", id, status);
            TempData["ErrorMessage"] = "The betting record status could not be changed.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBankrollTransaction(
        BankrollTransactionFormViewModel form,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "The bankroll transaction is invalid.";
            return RedirectToAction(nameof(Index), new { currencyCode = NormalizeCurrency(form.CurrencyCode) });
        }

        try
        {
            await _bettingApiClient.CreateBankrollTransactionAsync(form, cancellationToken);
            TempData["SuccessMessage"] = "Bankroll transaction saved.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not create bankroll transaction");
            TempData["ErrorMessage"] = "The bankroll transaction could not be saved.";
        }

        return RedirectToAction(nameof(Index), new { currencyCode = NormalizeCurrency(form.CurrencyCode) });
    }

    private static BettingRecordFormViewModel ToForm(BettingRecordViewModel record)
    {
        return new BettingRecordFormViewModel
        {
            Id = record.Id,
            League = record.League,
            Season = record.Season,
            MatchDate = record.MatchDate,
            HomeTeam = record.HomeTeam,
            AwayTeam = record.AwayTeam,
            Bookmaker = record.Bookmaker,
            MarketType = record.MarketType,
            BetSelection = record.BetSelection,
            CurrencyCode = record.CurrencyCode,
            Line = record.Line,
            Odds = record.Odds,
            Stake = record.Stake,
            Status = record.Status,
            ActualHomeCorners = record.ActualHomeCorners,
            ActualAwayCorners = record.ActualAwayCorners,
            ActualTotalCorners = record.ActualTotalCorners,
            ActualHomeShots = record.ActualHomeShots,
            ActualAwayShots = record.ActualAwayShots,
            ActualTotalShots = record.ActualTotalShots,
            ActualHomeShotsOnGoal = record.ActualHomeShotsOnGoal,
            ActualAwayShotsOnGoal = record.ActualAwayShotsOnGoal,
            ActualTotalShotsOnGoal = record.ActualTotalShotsOnGoal,
            CashoutAmount = record.CashoutAmount,
            BankrollBefore = record.BankrollBefore,
            ClosingOdds = record.ClosingOdds,
            ConfidenceLevel = record.ConfidenceLevel,
            PredictionModel = record.PredictionModel,
            Notes = record.Notes
        };
    }

    private async Task<BettingIndexViewModel> BuildIndexViewModelAsync(
        BettingFiltersViewModel filters,
        CancellationToken cancellationToken)
    {
        filters.CurrencyCode = NormalizeOptionalCurrency(filters.CurrencyCode);
        var workingCurrencyCode = filters.CurrencyCode ?? "CLP";
        var records = await _bettingApiClient.GetAsync(filters, cancellationToken);
        var summary = await _bettingApiClient.GetSummaryAsync(filters, cancellationToken);
        var pendingExposureSummary = await _bettingApiClient.GetSummaryAsync(
            new BettingFiltersViewModel
            {
                CurrencyCode = workingCurrencyCode,
                Status = "Pending"
            },
            cancellationToken);
        var currentBankroll = await _bettingApiClient.GetCurrentBankrollAsync(workingCurrencyCode, cancellationToken);
        var bankrollTransactions = await _bettingApiClient.GetBankrollTransactionsAsync(workingCurrencyCode, cancellationToken);
        var pendingStake = pendingExposureSummary.TotalStake;

        return new BettingIndexViewModel
        {
            Filters = filters,
            WorkingCurrencyCode = workingCurrencyCode,
            Records = records,
            Summary = summary,
            CurrentBankroll = currentBankroll,
            PendingStake = pendingStake,
            AvailableBankroll = currentBankroll - pendingStake,
            BankrollTransactions = bankrollTransactions,
            BankrollForm = new BankrollTransactionFormViewModel
            {
                CurrencyCode = workingCurrencyCode
            }
        };
    }

    private static string NormalizeCurrency(string? currencyCode)
    {
        return BettingOptions.CurrencyCodes.FirstOrDefault(item =>
            item.Equals(currencyCode, StringComparison.OrdinalIgnoreCase)) ?? "CLP";
    }

    private static string? NormalizeOptionalCurrency(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return null;
        }

        return BettingOptions.CurrencyCodes.FirstOrDefault(item =>
            item.Equals(currencyCode, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeOption(string? value, IReadOnlyCollection<string> options, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return options.FirstOrDefault(item =>
            item.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }
}
