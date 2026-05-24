using CornersPrediction.Application.Betting;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/betting")]
public sealed class BettingController : ControllerBase
{
    private readonly ICreateBettingRecordUseCase _createUseCase;
    private readonly IUpdateBettingRecordUseCase _updateUseCase;
    private readonly IDeleteBettingRecordUseCase _deleteUseCase;
    private readonly IGetBettingRecordByIdUseCase _getByIdUseCase;
    private readonly IGetBettingRecordsUseCase _getRecordsUseCase;
    private readonly IGetBettingSummaryUseCase _getSummaryUseCase;
    private readonly ICreateBankrollTransactionUseCase _createBankrollTransactionUseCase;
    private readonly IGetBankrollTransactionsUseCase _getBankrollTransactionsUseCase;
    private readonly IGetCurrentBankrollUseCase _getCurrentBankrollUseCase;
    private readonly ILogger<BettingController> _logger;

    public BettingController(
        ICreateBettingRecordUseCase createUseCase,
        IUpdateBettingRecordUseCase updateUseCase,
        IDeleteBettingRecordUseCase deleteUseCase,
        IGetBettingRecordByIdUseCase getByIdUseCase,
        IGetBettingRecordsUseCase getRecordsUseCase,
        IGetBettingSummaryUseCase getSummaryUseCase,
        ICreateBankrollTransactionUseCase createBankrollTransactionUseCase,
        IGetBankrollTransactionsUseCase getBankrollTransactionsUseCase,
        IGetCurrentBankrollUseCase getCurrentBankrollUseCase,
        ILogger<BettingController> logger)
    {
        _createUseCase = createUseCase;
        _updateUseCase = updateUseCase;
        _deleteUseCase = deleteUseCase;
        _getByIdUseCase = getByIdUseCase;
        _getRecordsUseCase = getRecordsUseCase;
        _getSummaryUseCase = getSummaryUseCase;
        _createBankrollTransactionUseCase = createBankrollTransactionUseCase;
        _getBankrollTransactionsUseCase = getBankrollTransactionsUseCase;
        _getCurrentBankrollUseCase = getCurrentBankrollUseCase;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BettingRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] string? currencyCode,
        [FromQuery] string? league,
        [FromQuery] string? season,
        [FromQuery] string? homeTeam,
        [FromQuery] string? awayTeam,
        [FromQuery] string? status,
        [FromQuery] string? marketType,
        [FromQuery] string? bookmaker,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken cancellationToken)
    {
        var filters = new BettingFiltersRequest(currencyCode, league, season, homeTeam, awayTeam, status, marketType, bookmaker, dateFrom, dateTo);
        var records = await _getRecordsUseCase.GetAsync(filters, cancellationToken);
        return Ok(records);
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(BettingSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(
        [FromQuery] string? currencyCode,
        [FromQuery] string? league,
        [FromQuery] string? season,
        [FromQuery] string? homeTeam,
        [FromQuery] string? awayTeam,
        [FromQuery] string? status,
        [FromQuery] string? marketType,
        [FromQuery] string? bookmaker,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken cancellationToken)
    {
        var filters = new BettingFiltersRequest(currencyCode, league, season, homeTeam, awayTeam, status, marketType, bookmaker, dateFrom, dateTo);
        var summary = await _getSummaryUseCase.GetAsync(filters, cancellationToken);
        return Ok(summary);
    }

    [HttpGet("bankroll")]
    [ProducesResponseType(typeof(IReadOnlyList<BankrollTransactionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBankrollTransactions(
        [FromQuery] string? currencyCode,
        CancellationToken cancellationToken)
    {
        var transactions = await _getBankrollTransactionsUseCase.GetAsync(currencyCode, cancellationToken);
        return Ok(transactions);
    }

    [HttpGet("bankroll/current")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentBankroll(
        [FromQuery] string? currencyCode,
        CancellationToken cancellationToken)
    {
        var currentBankroll = await _getCurrentBankrollUseCase.GetAsync(currencyCode, cancellationToken);
        return Ok(new { currencyCode = currencyCode ?? "CLP", currentBankroll });
    }

    [HttpPost("bankroll")]
    [ProducesResponseType(typeof(BankrollTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateBankrollTransaction(
        [FromBody] CreateBankrollTransactionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        try
        {
            var created = await _createBankrollTransactionUseCase.CreateAsync(request, cancellationToken);
            return Created($"/api/betting/bankroll/{created.Id}", created);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to create bankroll transaction");
            return Problem(title: "Could not save bankroll transaction", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(BettingRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] long id, CancellationToken cancellationToken)
    {
        try
        {
            var record = await _getByIdUseCase.GetAsync(id, cancellationToken);
            return record is null ? NotFound(new { error = "Betting record was not found." }) : Ok(record);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(BettingRecordDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBettingRecordRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        try
        {
            var created = await _createUseCase.CreateAsync(request, cancellationToken);
            return Created($"/api/betting/{created.Id}", created);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to create betting record");
            return Problem(title: "Could not save betting record", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        [FromRoute] long id,
        [FromBody] UpdateBettingRecordRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        try
        {
            var rowsAffected = await _updateUseCase.UpdateAsync(id, request, cancellationToken);
            return rowsAffected == 0 ? NotFound(new { error = "Betting record was not found." }) : NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update betting record {BettingRecordId}", id);
            return Problem(title: "Could not update betting record", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] long id, CancellationToken cancellationToken)
    {
        try
        {
            var rowsAffected = await _deleteUseCase.DeleteAsync(id, cancellationToken);
            return rowsAffected == 0 ? NotFound(new { error = "Betting record was not found." }) : NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to delete betting record {BettingRecordId}", id);
            return Problem(title: "Could not delete betting record", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
