namespace CornersPrediction.Web.Services;

public sealed class CurrentUserApiHeaderHandler : DelegatingHandler
{
    private const string UserIdHeaderName = "X-User-Id";
    private readonly ICurrentUserService _currentUserService;

    public CurrentUserApiHeaderHandler(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.IsAuthenticated &&
            !request.Headers.Contains(UserIdHeaderName) &&
            !string.IsNullOrWhiteSpace(_currentUserService.BettingUserId))
        {
            request.Headers.TryAddWithoutValidation(UserIdHeaderName, _currentUserService.BettingUserId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
