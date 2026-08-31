using CornersPrediction.Web.Clients;
using CornersPrediction.Web.Options;
using CornersPrediction.Web.Services;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

LoadDotEnv(builder.Environment.ContentRootPath);
builder.Configuration.AddInMemoryCollection(BuildAzureAdConfigurationFromEnvironment());

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddMicrosoftIdentityWebApp(
        builder.Configuration.GetSection("AzureAd"),
        OpenIdConnectDefaults.AuthenticationScheme,
        CookieAuthenticationDefaults.AuthenticationScheme);

builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.SaveTokens = true;
    options.Events.OnTokenValidated = async context =>
    {
        if (context.Principal is null)
        {
            context.Fail("Microsoft sign-in did not return a principal.");
            return;
        }

        var signInService = context.HttpContext.RequestServices
            .GetRequiredService<IPlatformUserSignInService>();
        var result = await signInService.ValidateAsync(
            context.Principal,
            context.HttpContext.RequestAborted);

        if (!result.IsAllowed || result.User is null)
        {
            context.Fail(result.ErrorMessage ?? "The Microsoft account is not enabled in this platform.");
            return;
        }

        if (context.Principal.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim("platform_user_id", result.User.Id.ToString()));

            if (!string.IsNullOrWhiteSpace(result.ExternalUserId))
            {
                identity.AddClaim(new Claim("platform_external_user_id", result.ExternalUserId));
            }

            if (!identity.HasClaim(claim => claim.Type == ClaimTypes.Email) &&
                !string.IsNullOrWhiteSpace(result.Email))
            {
                identity.AddClaim(new Claim(ClaimTypes.Email, result.Email));
            }

            foreach (var role in result.User.Roles.Where(role => !string.IsNullOrWhiteSpace(role)))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
    };
    options.Events.OnRemoteFailure = context =>
    {
        context.HandleResponse();
        context.Response.Redirect("/Account/Login?ssoError=1");
        return Task.CompletedTask;
    };
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy(PlatformPolicies.Admin, policy =>
        policy.RequireRole("Admin"));
    options.AddPolicy(PlatformPolicies.Betting, policy =>
        policy.RequireAuthenticatedUser());
    options.AddPolicy(PlatformPolicies.Predictions, policy =>
        policy.RequireRole("Admin", "Analyst", "User"));
});

builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IPlatformUserSignInService, PlatformUserSignInService>();
builder.Services.AddTransient<CurrentUserApiHeaderHandler>();
builder.Services.AddTransient<InternalApiKeyHeaderHandler>();

builder.Services.Configure<BackendApiOptions>(
    builder.Configuration.GetSection(BackendApiOptions.SectionName));

builder.Services.AddHttpClient<MatchHistoryApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

builder.Services.AddHttpClient<NewGenerationPredictionApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

builder.Services.AddHttpClient<BettingApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>()
.AddHttpMessageHandler<CurrentUserApiHeaderHandler>();

builder.Services.AddHttpClient<UserAdminApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

builder.Services.AddHttpClient<UpcomingMatchesApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

builder.Services.AddHttpClient<AutomatedCornersApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(10);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

builder.Services.AddHttpClient<BotG2026ApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    // The initial dashboard does not wait for scorecards. The lazy scorecard
    // component owns its timeout and may legitimately need more than 30s.
    client.Timeout = TimeSpan.FromSeconds(90);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

builder.Services.AddHttpClient<BotH2026ApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(3);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

builder.Services.AddHttpClient<RecommendationAutomationApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(2);
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

builder.Services.AddHttpClient<CornersPipelineApiClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    // Each backend pipeline step owns its timeout; a global client timeout can cancel a valid full run.
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.AddHttpMessageHandler<InternalApiKeyHeaderHandler>();

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();

static void LoadDotEnv(string contentRootPath)
{
    var candidatePaths = new[]
    {
        Path.Combine(contentRootPath, ".env"),
        Path.GetFullPath(Path.Combine(contentRootPath, "..", ".env"))
    };

    var envPath = candidatePaths.FirstOrDefault(File.Exists);
    if (envPath is not null)
    {
        Env.Load(envPath);
    }
}

static IDictionary<string, string?> BuildAzureAdConfigurationFromEnvironment()
{
    return new Dictionary<string, string?>
    {
        ["AzureAd:Instance"] = Environment.GetEnvironmentVariable("AZURE_AD_INSTANCE"),
        ["AzureAd:TenantId"] = Environment.GetEnvironmentVariable("AZURE_AD_TENANT_ID"),
        ["AzureAd:ClientId"] = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_ID"),
        ["AzureAd:ClientSecret"] = Environment.GetEnvironmentVariable("AZURE_AD_CLIENT_SECRET"),
        ["AzureAd:CallbackPath"] = Environment.GetEnvironmentVariable("AZURE_AD_CALLBACK_PATH"),
        ["BackendApi:BaseUrl"] = Environment.GetEnvironmentVariable("BACKEND_API_BASE_URL"),
        ["BackendApi:InternalApiKey"] = Environment.GetEnvironmentVariable("BACKEND_API_INTERNAL_KEY")
            ?? Environment.GetEnvironmentVariable("INTERNAL_API_KEY")
    }
    .Where(setting => !string.IsNullOrWhiteSpace(setting.Value))
    .ToDictionary(setting => setting.Key, setting => setting.Value);
}
