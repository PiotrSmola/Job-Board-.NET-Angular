using System.Globalization;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

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
// użytkownika (okno stałe, bez kolejki), przekroczenie -> 429 z komunikatem API i Retry-After (czas do końca okna).
// Limitery nieaktywnych użytkowników są usuwane przez PartitionedRateLimiter (brak nieograniczonego cache'u).
builder.Services.AddOptions<RateLimitingSettings>()
    .Bind(builder.Configuration.GetSection(RateLimitingSettings.SectionName))
    .PostConfigure(settings => settings.Validate());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitingSettings.UploadsPolicy, httpContext =>
    {
        var limit = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value.Uploads;
        // endpoint wymaga zalogowania - adres IP tylko obronnie, gdyby polityka trafiła na endpoint anonimowy
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? "ip:" + httpContext.Connection.RemoteIpAddress;

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = TimeSpan.FromSeconds(limit.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
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

// Nieobsłużone wyjątki -> 500 {message} bez szczegółów (GlobalExceptionHandler), także w Development
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

// konfiguracja potoku HTTP - obsługa wyjątków jako pierwsza (obejmuje cały potok)
app.UseExceptionHandler();

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
