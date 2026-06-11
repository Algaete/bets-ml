using CornersPrediction.Application.Admin;
using Microsoft.AspNetCore.Mvc;

namespace CornersPredictionApi.Controllers;

[ApiController]
[Route("api/admin/users")]
// TODO: Enable after SSO is configured: [Authorize(Roles = "Admin")]
public sealed class UserAdminController : ControllerBase
{
    private readonly ICreatePlatformUserUseCase _createUseCase;
    private readonly IUpdatePlatformUserUseCase _updateUseCase;
    private readonly IDeletePlatformUserUseCase _deleteUseCase;
    private readonly IGetPlatformUserByIdUseCase _getByIdUseCase;
    private readonly IGetPlatformUsersUseCase _getUsersUseCase;
    private readonly IGetPlatformRolesUseCase _getRolesUseCase;
    private readonly ILogger<UserAdminController> _logger;

    public UserAdminController(
        ICreatePlatformUserUseCase createUseCase,
        IUpdatePlatformUserUseCase updateUseCase,
        IDeletePlatformUserUseCase deleteUseCase,
        IGetPlatformUserByIdUseCase getByIdUseCase,
        IGetPlatformUsersUseCase getUsersUseCase,
        IGetPlatformRolesUseCase getRolesUseCase,
        ILogger<UserAdminController> logger)
    {
        _createUseCase = createUseCase;
        _updateUseCase = updateUseCase;
        _deleteUseCase = deleteUseCase;
        _getByIdUseCase = getByIdUseCase;
        _getUsersUseCase = getUsersUseCase;
        _getRolesUseCase = getRolesUseCase;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformUserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var users = await _getUsersUseCase.GetAsync(
            new PlatformUserFiltersRequest(search, role, isActive),
            cancellationToken);

        return Ok(users);
    }

    [HttpGet("roles")]
    [ProducesResponseType(typeof(IReadOnlyList<PlatformRoleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        return Ok(await _getRolesUseCase.GetAsync(cancellationToken));
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(PlatformUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] long id, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _getByIdUseCase.GetAsync(id, cancellationToken);
            return user is null ? NotFound(new { error = "User was not found." }) : Ok(user);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(PlatformUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePlatformUserRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        try
        {
            var created = await _createUseCase.CreateAsync(request, cancellationToken);
            return Created($"/api/admin/users/{created.Id}", created);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not create platform user");
            return Problem(title: "Could not create platform user", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        [FromRoute] long id,
        [FromBody] UpdatePlatformUserRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required." });
        }

        try
        {
            var rowsAffected = await _updateUseCase.UpdateAsync(id, request, cancellationToken);
            return rowsAffected == 0 ? NotFound(new { error = "User was not found." }) : NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not update platform user {PlatformUserId}", id);
            return Problem(title: "Could not update platform user", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
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
            return rowsAffected == 0 ? NotFound(new { error = "User was not found." }) : NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not delete platform user {PlatformUserId}", id);
            return Problem(title: "Could not delete platform user", detail: exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
