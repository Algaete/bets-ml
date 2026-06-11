using CornersPrediction.Application;
using CornersPrediction.Infrastructure;
using CornersPrediction.Infrastructure.Persistence;
using DotNetEnv;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

LoadDotEnv(builder.Environment.ContentRootPath);
builder.Configuration.AddInMemoryCollection(BuildDeploymentConfigurationFromEnvironment());

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.DictionaryKeyPolicy = null;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

var internalApiKey = app.Configuration["ApiSecurity:InternalApiKey"];
if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(internalApiKey))
{
    throw new InvalidOperationException("INTERNAL_API_KEY must be configured for non-development API deployments.");
}

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    if (string.IsNullOrWhiteSpace(internalApiKey) ||
        context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Internal-Api-Key", out var providedKey) ||
        !ApiKeysMatch(internalApiKey, providedKey.FirstOrDefault()))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid internal API key." });
        return;
    }

    await next();
});

var swaggerEnabled = app.Environment.IsDevelopment() ||
    app.Configuration.GetValue<bool>("Swagger:Enabled");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Corners Prediction API v1");
    });
}

app.MapControllers();

await DatabaseInitializer.EnsureDatabaseCreatedAsync(app.Services);

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

static IDictionary<string, string?> BuildDeploymentConfigurationFromEnvironment()
{
    return new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] =
            Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING"),
        ["ApiSecurity:InternalApiKey"] = Environment.GetEnvironmentVariable("INTERNAL_API_KEY"),
        ["Swagger:Enabled"] = Environment.GetEnvironmentVariable("SWAGGER_ENABLED"),
        ["PythonPrediction:PythonExecutable"] = Environment.GetEnvironmentVariable("PYTHON_EXECUTABLE"),
        ["PythonPrediction:ScriptPath"] = Environment.GetEnvironmentVariable("PYTHON_PREDICT_SCRIPT_PATH"),
        ["PythonPrediction:ShotsOnGoalScriptPath"] = Environment.GetEnvironmentVariable("PYTHON_SHOTS_SOG_SCRIPT_PATH"),
        ["PythonPrediction:ShotsOnGoalModelPath"] = Environment.GetEnvironmentVariable("PYTHON_SHOTS_SOG_MODEL_PATH"),
        ["PythonPrediction:ModelDebugScriptPath"] = Environment.GetEnvironmentVariable("PYTHON_MODEL_DEBUG_SCRIPT_PATH"),
        ["PythonPrediction:ModelDebugModelPath"] = Environment.GetEnvironmentVariable("PYTHON_MODEL_DEBUG_MODEL_PATH")
    }
    .Where(setting => !string.IsNullOrWhiteSpace(setting.Value))
    .ToDictionary(setting => setting.Key, setting => setting.Value);
}

static bool ApiKeysMatch(string expected, string? provided)
{
    if (string.IsNullOrWhiteSpace(provided))
    {
        return false;
    }

    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var providedBytes = Encoding.UTF8.GetBytes(provided);

    return expectedBytes.Length == providedBytes.Length &&
        CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
}
