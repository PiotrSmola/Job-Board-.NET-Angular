using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Backend.Data;
using Backend.Helpers;
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
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddSingleton<LoginGuard>();

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

// Ograniczanie liczby żądań (RateLimitingSettings, okno stałe od pierwszego żądania partycji - WindowRateLimiter):
// - "uploads" (upload załączników) - osobny limit dla każdego zalogowanego użytkownika i wspólny limit równoległych uploadów,
// - "auth" (logowanie, rejestracja - cały AuthController) - osobny limit dla każdego adresu IP i wspólny limit
//   równoległych żądań (haszowanie haseł obciąża CPU),
// - "public-lists" (publiczne listy ofert i firm) - osobny limit dla każdego adresu IP.
// Bez kolejek; przekroczenie -> 429 z komunikatem API i Retry-After (czas do końca okna, przy limicie równoległości 1 s).
// Limitery nieaktywnych partycji są usuwane przez PartitionedRateLimiter (brak nieograniczonego cache'u).
builder.Services.AddOptions<RateLimitingSettings>()
    .Bind(builder.Configuration.GetSection(RateLimitingSettings.SectionName))
    .PostConfigure(settings => settings.Validate());

// wspólne dla wszystkich partycji limity równoległych żądań (singletony zwalniane razem z kontenerem DI)
builder.Services.AddKeyedSingleton<RateLimiter>(RateLimitingSettings.UploadsPolicy, (sp, _) =>
    new ConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = sp.GetRequiredService<IOptions<RateLimitingSettings>>().Value.Uploads.ConcurrencyLimit,
        QueueLimit = 0
    }));
builder.Services.AddKeyedSingleton<RateLimiter>(RateLimitingSettings.AuthPolicy, (sp, _) =>
    new ConcurrencyLimiter(new ConcurrencyLimiterOptions
    {
        PermitLimit = sp.GetRequiredService<IOptions<RateLimitingSettings>>().Value.Auth.ConcurrencyLimit,
        QueueLimit = 0
    }));

// Adres klienta za reverse proxy: backend jest osiągalny tylko przez nginx z kontenera frontendu (sieć compose) i na
// 127.0.0.1 hosta, więc X-Forwarded-For jest przyjmowany od każdego nadawcy (KnownNetworks/KnownProxies wyczyszczone -
// adres nginx w sieci Dockera nie jest stały). ForwardLimit = 1: brany jest tylko ostatni wpis, czyli adres dopisany
// przez nginx ($proxy_add_x_forwarded_for) - wpisy podrobione przez klienta przed nim są ignorowane. Bez proxy (żądanie
// wprost na 127.0.0.1) lokalny klient mógłby podać dowolny adres - akceptowalne dla stacku dostępnego tylko lokalnie.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitingSettings.UploadsPolicy, httpContext =>
    {
        var services = httpContext.RequestServices;
        var concurrency = services.GetRequiredKeyedService<RateLimiter>(RateLimitingSettings.UploadsPolicy);
        var limit = services.GetRequiredService<IOptions<RateLimitingSettings>>().Value.Uploads;
        // endpoint wymaga zalogowania - adres IP tylko obronnie, gdyby polityka trafiła na endpoint anonimowy
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.ClientIpPartitionKey();

        return RateLimitPartition.Get(partitionKey, _ => new WindowRateLimiter(
            limit.PermitLimit,
            TimeSpan.FromSeconds(limit.WindowSeconds),
            concurrency,
            TimeProvider.System));
    });

    options.AddPolicy(RateLimitingSettings.AuthPolicy, httpContext =>
    {
        var services = httpContext.RequestServices;
        var concurrency = services.GetRequiredKeyedService<RateLimiter>(RateLimitingSettings.AuthPolicy);
        var limit = services.GetRequiredService<IOptions<RateLimitingSettings>>().Value.Auth;

        return RateLimitPartition.Get(httpContext.ClientIpPartitionKey(), _ => new WindowRateLimiter(
            limit.PermitLimit,
            TimeSpan.FromSeconds(limit.WindowSeconds),
            concurrency,
            TimeProvider.System));
    });

    options.AddPolicy(RateLimitingSettings.PublicListsPolicy, httpContext =>
    {
        var limit = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value.PublicLists;

        return RateLimitPartition.Get(httpContext.ClientIpPartitionKey(), _ => new WindowRateLimiter(
            limit.PermitLimit,
            TimeSpan.FromSeconds(limit.WindowSeconds),
            shared: null,
            TimeProvider.System));
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        // odrzucenie przez limit równoległości nie ma czasu do końca okna - klient może spróbować po sekundzie
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var windowLeft)
            ? Math.Max(1, (int)Math.Ceiling(windowLeft.TotalSeconds))
            : 1;
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = RateLimitingSettings.RejectedMessage }, cancellationToken);
    };
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

// konfiguracja potoku HTTP - najpierw prawdziwy adres klienta z X-Forwarded-For (nginx), z którego korzysta reszta
// potoku (limity żądań per IP); middleware nie rzuca wyjątków
app.UseForwardedHeaders();

// obsługa wyjątków (obejmuje resztę potoku); zaraz za nią błędy klienta
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

// po uwierzytelnieniu - limit uploadów liczony per użytkownik; anonim dostaje 401, zanim zostanie policzony
app.UseRateLimiter();

app.MapControllers();

app.Run();

// udostępnia klasę Program dla testów integracyjnych (WebApplicationFactory)
public partial class Program { }
