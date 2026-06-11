using CornersPrediction.Web.Options;
using Microsoft.Extensions.Options;

namespace CornersPrediction.Web.Services;

public sealed class InternalApiKeyHeaderHandler : DelegatingHandler
{
    private const string HeaderName = "X-Internal-Api-Key";
    private readonly BackendApiOptions _options;

    public InternalApiKeyHeaderHandler(IOptions<BackendApiOptions> options)
    {
        _options = options.Value;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.InternalApiKey) &&
            !request.Headers.Contains(HeaderName))
        {
            request.Headers.TryAddWithoutValidation(HeaderName, _options.InternalApiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
