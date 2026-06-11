using System.Security.Claims;
using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Models.Admin;

namespace CornersPrediction.Web.Services;

public static class PlatformPolicies
{
    public const string Admin = "PlatformAdmin";
    public const string Betting = "PlatformBetting";
    public const string Predictions = "PlatformPredictions";
}

public interface IPlatformUserSignInService
{
    Task<PlatformSignInResult> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed record PlatformSignInResult(
    bool IsAllowed,
    PlatformUserViewModel? User,
    string? Email,
    string? ExternalUserId,
    string? ErrorMessage);

public sealed class PlatformUserSignInService : IPlatformUserSignInService
{
    private readonly UserAdminApiClient _userAdminApiClient;
    private readonly ILogger<PlatformUserSignInService> _logger;

    public PlatformUserSignInService(
        UserAdminApiClient userAdminApiClient,
        ILogger<PlatformUserSignInService> logger)
    {
        _userAdminApiClient = userAdminApiClient;
        _logger = logger;
    }

    public async Task<PlatformSignInResult> ValidateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var email = GetEmail(principal);
        var externalUserId = GetExternalUserId(principal);

        if (string.IsNullOrWhiteSpace(email))
        {
            return new PlatformSignInResult(
                false,
                null,
                null,
                externalUserId,
                "Microsoft sign-in did not return an email claim.");
        }

        try
        {
            var platformUser = await _userAdminApiClient.FindActiveByEmailAsync(email, cancellationToken);
            if (platformUser is null)
            {
                return new PlatformSignInResult(
                    false,
                    null,
                    email,
                    externalUserId,
                    "Your Microsoft account is not enabled in the platform user module.");
            }

            return new PlatformSignInResult(true, platformUser, email, externalUserId, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not validate SSO user {Email} against platform users", email);
            return new PlatformSignInResult(
                false,
                null,
                email,
                externalUserId,
                "The platform user module could not validate your Microsoft account.");
        }
    }

    private static string? GetEmail(ClaimsPrincipal principal)
    {
        return principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst("email")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.Identity?.Name;
    }

    private static string? GetExternalUserId(ClaimsPrincipal principal)
    {
        return principal.FindFirst("oid")?.Value
            ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
