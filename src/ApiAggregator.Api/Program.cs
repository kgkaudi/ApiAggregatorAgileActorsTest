using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using ApiAggregator.Api.Authentication;
using ApiAggregator.Api.Configuration;
using ApiAggregator.Api.Extensions;
using ApiAggregator.Api.Services;
using ApiAggregator.Api.Statistics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Configuration ----
builder.Services.Configure<AggregationOptions>(config.GetSection(AggregationOptions.SectionName));
builder.Services.Configure<StatsOptions>(config.GetSection(StatsOptions.SectionName));
builder.Services.Configure<AnomalyDetectionOptions>(config.GetSection(AnomalyDetectionOptions.SectionName));
builder.Services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));
builder.Services.AddOptions<JwtOptions>()
    .Bind(config.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ---- Core services ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IRequestStatsStore, InMemoryRequestStatsStore>();
builder.Services.AddSingleton<ISourceFetcher, SourceFetcher>();
builder.Services.AddScoped<IAggregationService, AggregationService>();
builder.Services.AddExternalDataSources(config);

// ---- Performance anomaly detection (background service) ----
builder.Services.AddSingleton<PerformanceAnomalyAnalyzer>();
if (config.GetValue(AnomalyDetectionOptions.SectionName + ":Enabled", true))
    builder.Services.AddHostedService<PerformanceAnomalyWorker>();

// ---- JWT authentication ----
var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

// ---- MVC / OpenAPI ----
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "API Aggregator", Version = "v1" });

    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);

    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Get a token from POST /api/auth/token, then paste it here."
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
