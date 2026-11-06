using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Backend.Data;
using Backend.Interfaces;
using Backend.Models;
using Backend.Repositories;
using Backend.Services;
using Backend.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Domyślny limit treści żądania: 1 MB (endpointy z plikami podnoszą go [RequestSizeLimit] - RequestBodyLimitMiddleware)
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = RequestBodyLimitMiddleware.DefaultLimit);

// Konfiguracja bazy danych MySQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))
    ));

// Konfiguracja Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;

    // blokada konta po 5 nieudanych próbach logowania na 5 minut
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Ustawienia JWT - sekret walidowany w jednym miejscu (JwtSettings.EnsureValidSecret)
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .PostConfigure<IHostEnvironment>((settings, environment) => settings.EnsureValidSecret(environment));

// Konfiguracja uwierzytelniania JWT
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
    {
        var jwtSettings = jwtOptions.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
        };

        // token usuniętego użytkownika lub z nieaktualnym security stampem jest odrzucany (401)
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = JwtUserValidator.ValidateAsync
        };
    });

// Konfiguracja CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              // Content-Disposition - nazwa pobieranego załącznika
              .WithExposedHeaders("Pagination", "Content-Disposition");
    });
});

// Repository Services
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IJobApplicationRepository, JobApplicationRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<ITokenService, TokenService>();

// Statystyki: agregaty w SQL, zliczanie wyświetleń ofert (deduplikacja HMAC bez surowego IP) i sprzątanie starych
// wpisów deduplikacji w tle; TimeProvider pozwala testom ustawić "dzisiaj"
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<IJobViewTracker, JobViewTracker>();
builder.Services.AddHostedService<JobViewCleanupService>();

// Magazyn plików załączników wg Storage:Provider (Local - katalog na dysku, S3 - magazyn zgodny z S3, np. SeaweedFS);
// konfiguracja walidowana w jednym miejscu (StorageSettings.Validate)
builder.Services.AddOptions<StorageSettings>()
    .Bind(builder.Configuration.GetSection(StorageSettings.SectionName))
    .PostConfigure<IHostEnvironment>((settings, environment) => settings.Validate(environment));

builder.Services.AddSingleton<IFileStorage>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<StorageSettings>>().Value;
    return settings.UsesS3
        ? new S3FileStorage(settings.S3, sp.GetRequiredService<ILogger<S3FileStorage>>())
        : new LocalFileStorage(settings.LocalPath!);
});

// Uprawnienia do zasobów (admin - wszystko, pracodawca - zasoby swojej firmy) sprawdzane w jednym miejscu
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccess, CurrentUserAccess>();

// Ograniczanie liczby żądań: polityka "uploads" (upload załączników) - osobny limit dla każdego zalogowanego
// użytkownika (okno stałe od pierwszego żądania, bez kolejki) i wspólny limit równoległych uploadów (bez kolejki);
// przekroczenie -> 429 z komunikatem API, po wyczerpaniu okna z Retry-After = czas do końca okna (UploadRateLimiter).
// Limitery nieaktywnych użytkowników są usuwane przez PartitionedRateLimiter (brak nieograniczonego cache'u).
builder.Services.AddOptions<RateLimitingSettings>()
    .Bind(builder.Configuration.GetSection(RateLimitingSettings.SectionName))
    .PostConfigure(settings => settings.Validate());

// wspólny dla wszystkich użytkowników limit równoległych uploadów (singleton zwalniany razem z kontenerem DI)
builder.Services.AddKeyedSingleton<RateLimiter>(RateLimitingSettings.UploadsPolicy, (sp, _) =>
    new ConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = sp.GetRequiredService<IOptions<RateLimitingSettings>>().Value.Uploads.ConcurrencyLimit,
        QueueLimit = 0
    }));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitingSettings.UploadsPolicy, httpContext =>
    {
        var services = httpContext.RequestServices;
        var concurrency = services.GetRequiredKeyedService<RateLimiter>(RateLimitingSettings.UploadsPolicy);
        var limit = services.GetRequiredService<IOptions<RateLimitingSettings>>().Value.Uploads;
        // endpoint wymaga zalogowania - adres IP tylko obronnie, gdyby polityka trafiła na endpoint anonimowy
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? "ip:" + httpContext.Connection.RemoteIpAddress;

        return RateLimitPartition.Get(partitionKey, _ => new UploadRateLimiter(
            limit.PermitLimit,
            TimeSpan.FromSeconds(limit.WindowSeconds),
            concurrency,
            TimeProvider.System));
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = RateLimitingSettings.RejectedMessage }, cancellationToken);
    };
});

// Adres klienta z X-Forwarded-For (jedno ogniwo) tylko od zaufanego proxy w sieci prywatnej (nginx w Dockerze);
// bez tego wszyscy anonimowi odwiedzający mieliby adres kontenera nginx (deduplikacja wyświetleń ofert po IP)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    foreach (var (prefix, length) in new[] { ("10.0.0.0", 8), ("172.16.0.0", 12), ("192.168.0.0", 16), ("fc00::", 7) })
    {
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(prefix), length));
    }
});

// Nieobsłużone wyjątki -> 500 {message} bez szczegółów (GlobalExceptionHandler), także w Development;
// BadHttpRequestException -> jego kod statusu (np. 413 {message:"Request body too large"})
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Kontrolery - odpowiedzi to wyłącznie DTO (bez cykli nawigacji encji EF)
builder.Services.AddControllers();

// Konfiguracja Swagger z obsługą JWT
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Job Board API",
        Version = "v1",
        Description = "ASP.NET Core Web API for Job Board Application"
    });

    // autentyfikacja JWT w Swaggerze
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token."
    });

    // Microsoft.OpenApi 2.x - wymaganie wskazuje schemat przez referencję do dokumentu
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var app = builder.Build();

// wczesna walidacja ustawień JWT - brak sekretu poza Development przerywa start aplikacji
var resolvedJwtSettings = app.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
if (resolvedJwtSettings.UsesDevelopmentSecret)
{
    app.Logger.LogWarning(
        "JwtSettings:Secret is not configured - using the built-in development key. Never use it outside local development.");
}

// wczesna walidacja magazynu plików (zła konfiguracja przerywa start) i przygotowanie katalogu / bucketu
var resolvedStorageSettings = app.Services.GetRequiredService<IOptions<StorageSettings>>().Value;
await app.Services.GetRequiredService<IFileStorage>().InitializeAsync();
app.Logger.LogInformation("File storage: {Provider}", resolvedStorageSettings.Provider);

// wczesna walidacja limitów żądań (zła konfiguracja przerywa start)
_ = app.Services.GetRequiredService<IOptions<RateLimitingSettings>>().Value;

// seedowanie bazy danych
using (var scope = app.Services.CreateScope())
{
    await DatabaseSeeder.SeedAsync(scope.ServiceProvider);
}

// adres klienta od zaufanego proxy - przed wszystkim, co z niego korzysta
app.UseForwardedHeaders();

// konfiguracja potoku HTTP - obsługa wyjątków jako pierwsza (obejmuje cały potok); zaraz za nią błędy klienta
// przy odczycie żądania (BadHttpRequestException) - ich kod statusu i log Warning zamiast 500 i Error
app.UseExceptionHandler();
app.UseMiddleware<ClientRequestErrorMiddleware>();

// limit treści żądania dopasowanego endpointu (domyślnie 1 MB, upload załączników 26 MB) - przed odczytem treści
app.UseMiddleware<RequestBodyLimitMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Job Board API v1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();

app.UseCors("AllowAngularApp");

app.UseAuthentication();
app.UseAuthorization();

// po uwierzytelnieniu - limit liczony per użytkownik; anonim dostaje 401, zanim zostanie policzony
app.UseRateLimiter();

app.MapControllers();

app.Run();

// udostępnia klasę Program dla testów integracyjnych (WebApplicationFactory)
public partial class Program { }
