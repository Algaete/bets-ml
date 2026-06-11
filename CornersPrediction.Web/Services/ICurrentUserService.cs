using System.Security.Claims;

namespace CornersPrediction.Web.Services;

public interface ICurrentUserService
{
    string? BettingUserId { get; }

    string? Email { get; }

    string? Name { get; }

    bool IsAuthenticated { get; }
}

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? BettingUserId =>
        User?.FindFirst("platform_user_id")?.Value
        ?? Email;

    public string? Email =>
        User?.FindFirst("preferred_username")?.Value
        ?? User?.FindFirst("email")?.Value
        ?? User?.FindFirst(ClaimTypes.Email)?.Value
        ?? User?.Identity?.Name;

    public string? Name =>
        User?.FindFirst("name")?.Value
        ?? User?.FindFirst(ClaimTypes.Name)?.Value
        ?? Email;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    // Betting uses the internal platform user id so bankroll and bet records stay isolated per SSO user.
}
