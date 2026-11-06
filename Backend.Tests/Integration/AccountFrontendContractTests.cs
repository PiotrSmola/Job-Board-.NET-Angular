using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Controllers;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Models;
using Backend.Services.Email;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Tests.Integration;

// Zgodność z klientem Angulara dla etapu "konto użytkownika" (AuthService, AccountService, SavedJobsService,
// strony /confirm-email i /reset-password, getApiErrorMessage): linki z e-maili parsowane jak przez router,
// żądania w kształcie wysyłanym przez przeglądarkę, kształt odpowiedzi, limity i komunikaty porównane ze źródłami frontendu
[Collection(ApiCollection.Name)]
public class AccountFrontendContractTests
{
    private const string NewPassword = "Brand-New-Passw0rd";

    private readonly ApiFixture _api;

    public AccountFrontendContractTests(ApiFixture api) => _api = api;

    // Adres względny linku, który dostaje router Angulara (bez schematu i hosta)
    private static string RouterUrl(Uri link) => link.OriginalString[JobBoardFactory.FrontendBaseUrl.Length..];

    // Claim z payloadu JWT dokładnie tak, jak czyta go frontend (decodeJwtPayload - bez mapowania nazw claimów)
    private static JsonElement JwtPayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(segment)).RootElement.Clone();
    }

    private static string[] PropertyNames(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    // --- linki z e-maili czytane jak router Angulara ---

    [Fact]
    public async Task ConfirmationLink_ReadLikeTheAngularRouter_ConfirmsTheAddress()
    {
        var user = await _api.RegisterUserAsync("ctr");
        var (link, _, _) = await _api.Emails.ConfirmationLinkAsync(user.Email);

        var query = AngularRouterQuery.Parse(RouterUrl(link));

        Assert.Equal(["token", "userId"], query.Keys.Order(StringComparer.Ordinal));
        // frontend porównuje userId z linku z claimem "nameid" tokena (bez mapowania nazw claimów)
        Assert.Equal(user.Id, query["userId"]);
        Assert.Equal(user.Id, JwtPayload(user.Token).GetProperty("nameid").GetString());
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ConfirmEmailAsync(query["userId"], query["token"])).StatusCode);
    }

    [Theory]
    [InlineData("plus+tag")]
    [InlineData("a+b+c")]
    [InlineData("Mixed.Case-name_1+x")]
    public async Task ResetLink_OfAnAddressWithAPlus_SurvivesTheAngularRouter(string localPart)
    {
        var email = $"{localPart}.{Guid.NewGuid():N}@test.local";
        using var anonymous = _api.CreateClient();
        var register = await anonymous.PostJsonAsync("/api/auth/register",
            new RegisterDto { Email = email, Password = "Passw0rd1", FirstName = "Plus", LastName = "Sign" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        await anonymous.ForgotPasswordAsync(email);

        var (link, _, _) = await _api.Emails.ResetLinkAsync(email);

        // surowy "+" router Angulara zamieniłby na spację - w linku musi być %2B
        var queryString = link.OriginalString[link.OriginalString.IndexOf('?')..];
        Assert.DoesNotContain("+", queryString);
        Assert.Contains("%2B", queryString);
        var query = AngularRouterQuery.Parse(RouterUrl(link));
        Assert.Equal(email, query["email"]);
        // kontrola samego pomocnika: ten sam adres z surowym plusem router czytałby błędnie
        Assert.Equal(email.Replace("+", " "), AngularRouterQuery.Parse("/reset-password?email=" + email)["email"]);

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ResetPasswordAsync(query["email"], query["token"], NewPassword)).StatusCode);
        var login = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ResetLink_OfAnAddressTypedInAnotherCase_StillWorks()
    {
        var user = await _api.RegisterUserAsync("Case");
        using var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.ForgotPasswordAsync(user.Email.ToUpperInvariant())).StatusCode);

        var (link, _, _) = await _api.Emails.ResetLinkAsync(user.Email);
        var query = AngularRouterQuery.Parse(RouterUrl(link));
        // frontend wysyła adres z linku, ale reset ma działać także dla adresu w innej wielkości liter
        Assert.Equal(HttpStatusCode.NoContent,
            (await anonymous.ResetPasswordAsync(query["email"].ToUpperInvariant(), query["token"], NewPassword)).StatusCode);
    }

    [Fact]
    public async Task EmailLinkParameters_FitTheLimitsTheFrontendChecks()
    {
        // najdłuższy możliwy adres (256 znaków) - frontend odrzuca dłuższe parametry bez wywołania API
        var email = new string('x', 256 - "@test.local".Length - 33) + $".{Guid.NewGuid():N}@test.local";
        Assert.Equal(256, email.Length);
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostJsonAsync("/api/auth/register",
            new RegisterDto { Email = email, Password = "Passw0rd1", FirstName = "Long", LastName = "Address" })).StatusCode);
        await anonymous.ForgotPasswordAsync(email);

        var (confirmLink, userId, confirmToken) = await _api.Emails.ConfirmationLinkAsync(email);
        var (_, resetEmail, resetToken) = await _api.Emails.ResetLinkAsync(email);

        var confirmPage = FrontendSource.Read("frontend/src/app/components/confirm-email/confirm-email.component.ts");
        var resetPage = FrontendSource.Read("frontend/src/app/components/reset-password/reset-password.component.ts");
        var maxToken = FrontendSource.Number(confirmPage, "MAX_TOKEN_LENGTH");
        Assert.Equal(maxToken, FrontendSource.Number(resetPage, "MAX_TOKEN_LENGTH"));
        Assert.Equal(EmailTokens.MaxEncodedLength, maxToken);
        Assert.Equal(typeof(ConfirmEmailDto).GetProperty(nameof(ConfirmEmailDto.UserId))!.GetCustomAttribute<MaxLengthAttribute>()!.Length,
            FrontendSource.Number(confirmPage, "MAX_USER_ID_LENGTH"));
        Assert.Equal(typeof(ResetPasswordDto).GetProperty(nameof(ResetPasswordDto.Email))!.GetCustomAttribute<MaxLengthAttribute>()!.Length,
            FrontendSource.Number(resetPage, "MAX_EMAIL_LENGTH"));

        // prawdziwe linki mieszczą się w limitach z dużym zapasem
        Assert.True(userId.Length <= FrontendSource.Number(confirmPage, "MAX_USER_ID_LENGTH"));
        Assert.True(confirmToken.Length <= maxToken / 2, $"confirmation token has {confirmToken.Length} characters");
        Assert.True(resetToken.Length <= maxToken / 2, $"reset token has {resetToken.Length} characters");
        Assert.True(resetEmail.Length <= FrontendSource.Number(resetPage, "MAX_EMAIL_LENGTH"));
        Assert.Contains('@', resetEmail);
        // link o takim adresie nadal działa po stronie API
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ResetPasswordAsync(resetEmail, resetToken, NewPassword)).StatusCode);
        Assert.StartsWith(JobBoardFactory.FrontendBaseUrl + "/confirm-email?userId=", confirmLink.OriginalString);
    }

    // Frontend dołącza do każdego żądania /api/ token z localStorage - także wtedy, gdy jest już nieważny
    // (hasło zmienione w innej karcie). Endpointy bez [Authorize] muszą go zignorować, a nie odpowiedzieć 401
    // (401 wylogowałoby użytkownika w trakcie resetu hasła).
    [Theory]
    [InlineData("stale")]
    [InlineData("garbage")]
    public async Task InvalidBearerToken_DoesNotBlockTheAnonymousLinkEndpoints(string kind)
    {
        var user = await _api.RegisterUserAsync("stale");
        var (_, userId, confirmToken) = await _api.Emails.ConfirmationLinkAsync(user.Email);
        string token;
        if (kind == "stale")
        {
            using var other = _api.CreateClient(user);
            Assert.Equal(HttpStatusCode.OK, (await other.ChangePasswordAsync(user.Password, NewPassword)).StatusCode);
            token = user.Token;
            using var probe = _api.CreateClient(token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await probe.GetAsync(AccountApi.ProfileUrl)).StatusCode);
        }
        else
        {
            token = "not.a.jwt";
        }

        using var client = _api.CreateClient(token);

        // link potwierdzający sprzed zmiany hasła jest już nieważny, ale to zwykłe 400 z komunikatem, nie 401
        var confirm = await client.ConfirmEmailAsync(userId, confirmToken);
        if (kind == "stale")
        {
            Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
            Assert.Equal("Invalid or expired confirmation link", await confirm.ReadMessageAsync());
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Accepted, (await client.ForgotPasswordAsync(user.Email)).StatusCode);
        var (_, email, resetToken) = await _api.Emails.ResetLinkAsync(user.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await client.ResetPasswordAsync(email, resetToken, "Reset-Passw0rd")).StatusCode);
    }

    // --- kształt żądań wysyłanych przez przeglądarkę ---

    // Angular: http.delete('/api/account', { body: { password } }) - Content-Type: application/json (bez charset)
    [Fact]
    public async Task DeleteAccount_WithTheRequestAngularSends_Returns204()
    {
        var user = await _api.RegisterUserAsync("del");
        using var client = _api.CreateClient(user);
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/account")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { password = user.Password }), Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Accept.ParseAdd("application/json, text/plain, */*");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = user.Password })).StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WithoutAJsonBody_Returns415AndKeepsTheAccount()
    {
        var user = await _api.RegisterUserAsync("del");
        using var client = _api.CreateClient(user);

        var empty = await client.DeleteAsync("/api/account");
        var plain = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/account")
        {
            Content = new StringContent(user.Password, Encoding.UTF8, "text/plain")
        });

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, empty.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, plain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountApi.ProfileUrl)).StatusCode);
    }

    // Formularz zapisuje puste pola opcjonalne jako null - odpowiedź zwraca je jako null (nie pomija pól)
    [Fact]
    public async Task UpdateProfile_WithEveryOptionalFieldNull_Returns200WithExplicitNulls()
    {
        var user = await _api.RegisterUserAsync("prof");
        using var client = _api.CreateClient(user);

        var response = await client.PutRawJsonAsync(AccountApi.ProfileUrl,
            "{\"firstName\":\"Łukasz\",\"lastName\":\"Żółć\",\"phoneNumber\":null,\"headline\":null,\"location\":null,\"linkedInUrl\":null,\"about\":null}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var field in new[] { "phoneNumber", "headline", "location", "linkedInUrl", "about" })
        {
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty(field).ValueKind);
        }

        Assert.Equal("Łukasz", body.RootElement.GetProperty("firstName").GetString());
        Assert.Equal(user.Email, body.RootElement.GetProperty("email").GetString());
    }

    // JS /\S/ uznaje U+0085 (NEL) za znak niebiałych, a .NET za biały - imię z samego takiego znaku przejdzie
    // walidację formularza, ale serwer je odrzuci (400 ValidationProblem; frontend pokaże ogólny komunikat)
    [Fact]
    public async Task UpdateProfile_WithANameOfOnlyANextLineCharacter_Returns400()
    {
        var user = await _api.RegisterUserAsync("prof");
        using var client = _api.CreateClient(user);

        var response = await client.PutJsonAsync(AccountApi.ProfileUrl, new UpdateProfileDto { FirstName = "\u0085", LastName = "Kowalski" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("FirstName", await response.ReadValidationFieldsAsync());
    }

    // --- kształt odpowiedzi ---

    [Fact]
    public async Task UserResponses_HaveExactlyTheFieldsOfTheFrontendUserDto()
    {
        string[] expected = ["email", "emailConfirmed", "firstName", "lastName", "token"];
        var email = $"shape_{Guid.NewGuid():N}@test.local";
        using var anonymous = _api.CreateClient();

        var register = await anonymous.PostJsonAsync("/api/auth/register",
            new RegisterDto { Email = email, Password = "Passw0rd1", FirstName = "Sha", LastName = "Pe" });
        var registerJson = await register.Content.ReadAsStringAsync();
        Assert.Equal(expected, PropertyNames(registerJson));
        Assert.Equal(JsonValueKind.False, JsonDocument.Parse(registerJson).RootElement.GetProperty("emailConfirmed").ValueKind);

        // niepotwierdzony adres: logowanie też zwraca false (baner wraca po ponownym zalogowaniu)
        var login = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = "Passw0rd1" });
        var loginJson = await login.Content.ReadAsStringAsync();
        Assert.Equal(expected, PropertyNames(loginJson));
        Assert.Equal(JsonValueKind.False, JsonDocument.Parse(loginJson).RootElement.GetProperty("emailConfirmed").ValueKind);

        var (_, userId, token) = await _api.Emails.ConfirmationLinkAsync(email);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ConfirmEmailAsync(userId, token)).StatusCode);

        var confirmedLogin = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = "Passw0rd1" });
        var confirmedJson = await confirmedLogin.Content.ReadAsStringAsync();
        Assert.Equal(JsonValueKind.True, JsonDocument.Parse(confirmedJson).RootElement.GetProperty("emailConfirmed").ValueKind);

        using var client = _api.CreateClient(JsonDocument.Parse(confirmedJson).RootElement.GetProperty("token").GetString());
        var change = await client.ChangePasswordAsync("Passw0rd1", NewPassword);
        var changeJson = await change.Content.ReadAsStringAsync();
        Assert.Equal(expected, PropertyNames(changeJson));
        Assert.Equal(JsonValueKind.True, JsonDocument.Parse(changeJson).RootElement.GetProperty("emailConfirmed").ValueKind);
        // nowy token niesie to samo id użytkownika w claimie "nameid"
        Assert.Equal(userId, JwtPayload(JsonDocument.Parse(changeJson).RootElement.GetProperty("token").GetString()!).GetProperty("nameid").GetString());

        var employer = await anonymous.PostJsonAsync("/api/auth/register-employer", new RegisterEmployerDto
        {
            Email = $"shape_emp_{Guid.NewGuid():N}@test.local", Password = "Passw0rd1", FirstName = "Em", LastName = "Ployer",
            CompanyName = ApiFixture.UniqueName("Shape Co")
        });
        Assert.Equal(expected, PropertyNames(await employer.Content.ReadAsStringAsync()));
    }

    // Daty muszą mieć znacznik UTC ("Z") - bez niego przeglądarka potraktowałaby je jako czas lokalny
    [Fact]
    public async Task DatesInAccountResponses_AreUtcIsoStrings()
    {
        var user = await _api.RegisterUserAsync("dates");
        using var client = _api.CreateClient(user);
        var job = await _api.CreateJobAsync();
        await client.UploadDocumentsOkAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{AccountApi.SavedJobsUrl}/{job.Id}", null)).StatusCode);
        var utc = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$");

        using var profile = JsonDocument.Parse(await (await client.GetAsync(AccountApi.ProfileUrl)).Content.ReadAsStringAsync());
        using var documents = JsonDocument.Parse(await (await client.GetAsync(AccountApi.DocumentsUrl)).Content.ReadAsStringAsync());
        using var saved = JsonDocument.Parse(await (await client.GetAsync(AccountApi.SavedJobsUrl)).Content.ReadAsStringAsync());

        Assert.Matches(utc, profile.RootElement.GetProperty("createdAt").GetString()!);
        Assert.Matches(utc, documents.RootElement[0].GetProperty("uploadedAt").GetString()!);
        Assert.Matches(utc, saved.RootElement[0].GetProperty("savedAt").GetString()!);
    }

    // Frontend rozpoznaje błędy hasła po treści opisów z IdentityErrorDescriber (password.ts) - wzorce pochodzą
    // wprost ze źródła frontendu, więc zmiana opisu po którejkolwiek stronie wywróci ten test
    [Theory]
    [InlineData("abc")]
    [InlineData("abcdefg1")]
    [InlineData("ABCDEFG1")]
    [InlineData("Abcdefgh")]
    public async Task WeakPasswordErrors_AreAllRecognisedByTheFrontendTranslations(string weak)
    {
        var patterns = FrontendSource.PasswordErrorPatterns();
        Assert.Equal(6, patterns.Count);
        var user = await _api.RegisterUserAsync("weak");
        using var client = _api.CreateClient(user);
        using var anonymous = _api.CreateClient();
        await anonymous.ForgotPasswordAsync(user.Email);
        var (_, email, token) = await _api.Emails.ResetLinkAsync(user.Email);

        var change = await client.ChangePasswordAsync(user.Password, weak);
        var reset = await anonymous.ResetPasswordAsync(email, token, weak);

        foreach (var response in new[] { change, reset })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var errors = body.RootElement.GetProperty("errors");
            Assert.Equal(["NewPassword"], errors.EnumerateObject().Select(p => p.Name));
            var messages = errors.GetProperty("NewPassword").EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.NotEmpty(messages);
            Assert.All(messages, m => Assert.Contains(patterns, p => p.IsMatch(m)));
        }
    }

    // Reguły haseł klienta (6-100 znaków, mała i wielka litera, cyfra) przyjmują i odrzucają dokładnie to samo co Identity
    [Fact]
    public async Task PasswordRules_OfTheFrontendAndTheBackend_AcceptTheSamePasswords()
    {
        var passwordSource = FrontendSource.Read("frontend/src/app/utils/password.ts");
        var min = (int)FrontendSource.Number(passwordSource, "PASSWORD_MIN_LENGTH");
        var max = (int)FrontendSource.Number(passwordSource, "PASSWORD_MAX_LENGTH");
        bool FrontendAccepts(string p) => p.Length >= min && p.Length <= max
            && Regex.IsMatch(p, "[a-z]") && Regex.IsMatch(p, "[A-Z]") && Regex.IsMatch(p, @"[0-9]");

        var identity = _api.Factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Password;
        Assert.Equal(min, identity.RequiredLength);
        Assert.True(identity.RequireDigit && identity.RequireLowercase && identity.RequireUppercase);
        Assert.False(identity.RequireNonAlphanumeric);

        var candidates = new[]
        {
            "Abcde1", "Abcd1", "abcdef1", "ABCDEF1", "Abcdefg", "ąbcdef1", "Ąbcdef1", "Abcdef٣", "Abc de1", "Abcdef1!",
            "Aa1" + new string('x', max - 3), "Aa1" + new string('x', max - 2), "Aa1\tTab", new string('1', 6)
        };

        var user = await _api.RegisterUserAsync("rules");
        var current = user.Password;
        var client = _api.CreateClient(user);
        try
        {
            foreach (var candidate in candidates)
            {
                var response = await client.ChangePasswordAsync(current, candidate);
                Assert.True(FrontendAccepts(candidate) == (response.StatusCode == HttpStatusCode.OK),
                    $"'{candidate}': frontend accepts={FrontendAccepts(candidate)}, backend answered {(int)response.StatusCode}");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    current = candidate;
                    client.Dispose();
                    client = _api.CreateClient((await response.ReadJsonAsync<UserDto>()).Token);
                }
            }
        }
        finally
        {
            client.Dispose();
        }
    }

    // Limity formularzy i stałe frontendu są takie same jak walidacja backendu
    [Fact]
    public void FrontendLimitsAndPatterns_MatchTheBackendValidation()
    {
        static int MaxLength<T>(string property) =>
            typeof(T).GetProperty(property)!.GetCustomAttribute<MaxLengthAttribute>()!.Length;

        var profileSource = FrontendSource.Read("frontend/src/app/components/account/profile-tab/profile-tab.component.ts");
        var limits = new Dictionary<string, int>
        {
            ["firstName"] = MaxLength<UpdateProfileDto>(nameof(UpdateProfileDto.FirstName)),
            ["lastName"] = MaxLength<UpdateProfileDto>(nameof(UpdateProfileDto.LastName)),
            ["phoneNumber"] = MaxLength<UpdateProfileDto>(nameof(UpdateProfileDto.PhoneNumber)),
            ["headline"] = MaxLength<UpdateProfileDto>(nameof(UpdateProfileDto.Headline)),
            ["location"] = MaxLength<UpdateProfileDto>(nameof(UpdateProfileDto.Location)),
            ["linkedInUrl"] = MaxLength<UpdateProfileDto>(nameof(UpdateProfileDto.LinkedInUrl)),
            ["about"] = MaxLength<UpdateProfileDto>(nameof(UpdateProfileDto.About))
        };
        foreach (var (field, backend) in limits)
        {
            var match = Regex.Match(profileSource, @"\b" + field + @":\s*(\d+),?\s*\n");
            Assert.True(match.Success, $"PROFILE_LIMITS.{field} not found");
            Assert.Equal(backend, int.Parse(match.Groups[1].Value));
        }

        var passwordMax = (int)FrontendSource.Number(FrontendSource.Read("frontend/src/app/utils/password.ts"), "PASSWORD_MAX_LENGTH");
        Assert.Equal(passwordMax, MaxLength<ChangePasswordDto>(nameof(ChangePasswordDto.CurrentPassword)));
        Assert.Equal(passwordMax, MaxLength<ChangePasswordDto>(nameof(ChangePasswordDto.NewPassword)));
        Assert.Equal(passwordMax, MaxLength<ResetPasswordDto>(nameof(ResetPasswordDto.NewPassword)));
        Assert.Equal(passwordMax, MaxLength<DeleteAccountDto>(nameof(DeleteAccountDto.Password)));

        // adres e-mail w formularzu "nie pamiętasz hasła" (Validators.maxLength(256))
        var forgotSource = FrontendSource.Read("frontend/src/app/components/forgot-password/forgot-password.component.ts");
        Assert.Contains($"Validators.maxLength({MaxLength<ForgotPasswordDto>(nameof(ForgotPasswordDto.Email))})", forgotSource);

        // limity plików: te same dla aplikacji i profilu
        var attachments = FrontendSource.Read("frontend/src/app/utils/attachments.ts");
        Assert.Equal(AttachmentFiles.MaxFileSize, FrontendSource.Number(attachments, "ATTACHMENT_MAX_FILE_SIZE"));
        Assert.Equal(AttachmentFiles.MaxTotalSize, FrontendSource.Number(attachments, "ATTACHMENTS_MAX_TOTAL_SIZE"));
        Assert.Equal(AttachmentFiles.MaxProfileTotalSize, FrontendSource.Number(attachments, "ATTACHMENTS_MAX_TOTAL_SIZE"));
        Assert.Equal(AttachmentFiles.MaxAttachments, FrontendSource.Number(attachments, "ATTACHMENTS_MAX_COUNT"));
        Assert.Equal(AttachmentFiles.MaxProfileDocuments, FrontendSource.Number(attachments, "ATTACHMENTS_MAX_COUNT"));
    }

    // Wzorce telefonu i LinkedIn z formularza (JS) i z atrybutów DTO (.NET) przyjmują te same wartości;
    // JS "$" bez flagi m = koniec tekstu (w .NET "\z"), "\/" = "/"
    private static bool FrontendPatternMatches(string constant, bool ignoreCase, string value)
    {
        var source = FrontendSource.Read("frontend/src/app/components/account/profile-tab/profile-tab.component.ts");
        var pattern = Regex.Match(source, constant + @" = /(.+)/i?;").Groups[1].Value;
        Assert.NotEmpty(pattern);
        return new Regex(pattern.Replace(@"\/", "/").Replace("$", @"\z"), ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None)
            .IsMatch(value);
    }

    private static bool BackendPatternMatches(string property, string value) =>
        typeof(UpdateProfileDto).GetProperty(property)!.GetCustomAttribute<RegularExpressionAttribute>()!.IsValid(value);

    [Theory]
    [InlineData("+48 600-100-200", true)]
    [InlineData("600100200", true)]
    [InlineData("48 (22) 123", false)]
    [InlineData("tel. 600", false)]
    [InlineData("600\n", false)]
    [InlineData("٣٤", false)]
    public void PhonePattern_OfTheFrontendAndTheBackend_Agree(string value, bool valid)
    {
        Assert.Equal(valid, FrontendPatternMatches("PHONE_PATTERN", false, value));
        Assert.Equal(valid, BackendPatternMatches(nameof(UpdateProfileDto.PhoneNumber), value));
    }

    [Theory]
    [InlineData("https://www.linkedin.com/in/jan-kowalski", true)]
    [InlineData("https://linkedin.com/in/x", true)]
    [InlineData("HTTPS://WWW.LINKEDIN.COM/in/x", true)]
    [InlineData("http://linkedin.com/in/x", false)]
    [InlineData("https://linkedin.com/", false)]
    [InlineData("https://linkedin.com", false)]
    [InlineData("https://evil.com/linkedin.com/in/x", false)]
    [InlineData("https://linkedin.com.evil.com/in/x", false)]
    [InlineData("https://linkedin.com/in/x y", false)]
    [InlineData("https://linkedin.com/in/x\n", false)]
    public void LinkedInPattern_OfTheFrontendAndTheBackend_Agree(string value, bool valid)
    {
        Assert.Equal(valid, FrontendPatternMatches("LINKEDIN_PATTERN", true, value));
        Assert.Equal(valid, BackendPatternMatches(nameof(UpdateProfileDto.LinkedInUrl), value));
    }

    // Każdy komunikat błędu, który backend zwraca w etapie 4, ma polskie tłumaczenie w getApiErrorMessage
    [Fact]
    public void BackendMessagesOfTheAccountStage_AreTranslatedByTheFrontend()
    {
        var known = FrontendSource.KnownApiMessages();
        var messages = new[] { typeof(AuthController), typeof(AccountController), typeof(AttachmentFiles) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.Contains("Message", StringComparison.Ordinal))
            .Select(f => (Name: f.DeclaringType!.Name + "." + f.Name, Text: (string)f.GetRawConstantValue()!))
            .Append((Name: "SavedJobsController.JobNotFoundMessage", Text: "Job offer not found"))
            .Append((Name: "RateLimitingSettings.RejectedMessage", Text: Backend.Settings.RateLimitingSettings.RejectedMessage))
            .ToList();
        Assert.True(messages.Count >= 20, $"Only {messages.Count} messages found");

        var missing = messages.Where(m => !known.Contains(m.Text)).Select(m => $"{m.Name}: {m.Text}").ToList();
        Assert.True(missing.Count == 0, "Not translated by the frontend:\n" + string.Join("\n", missing));
    }

    // --- uploady dokumentów profilu ---

    [Fact]
    public async Task ProfileDocumentUpload_KeepsItsOwnLimitAboveOneMegabyte()
    {
        var user = await _api.RegisterUserAsync("big");
        using var client = _api.CreateClient(user);

        // trzy pliki po 5 MB w jednym żądaniu - ponad domyślny limit 1 MB, w limicie endpointu (26 MB)
        var response = await client.UploadDocumentsAsync(Enumerable.Range(0, 3).Select(i =>
            new AttachmentUpload(AttachmentTestFiles.PdfOfSize(AttachmentFiles.MaxFileSize - i), $"duzy{i}.pdf")).ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ProfileDocumentUpload_OverTheRequestLimit_Returns400WithTheProfileTotalMessage()
    {
        var user = await _api.RegisterUserAsync("huge");
        using var client = _api.CreateClient(user);

        // 6 plików po ~4,6 MB (każdy w limicie pliku) = ponad 26 MB w jednym żądaniu
        var response = await client.UploadDocumentsAsync(Enumerable.Range(0, 6).Select(i =>
            new AttachmentUpload(AttachmentTestFiles.PdfOfSize(4_600_000 + i), $"huge{i}.pdf")).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.ProfileTotalTooLargeMessage, await response.ReadMessageAsync());
        Assert.Contains(AttachmentFiles.ProfileTotalTooLargeMessage, FrontendSource.KnownApiMessages());
    }

    // --- brzegi, których frontend nie wysyła, ale przeglądarka/użytkownik może ---

    // Zakładka powiadomień jest dostępna dla każdej roli (pracodawca i admin też mają GET/PUT); admin nie wywołuje GET,
    // ale API nie może odpowiedzieć 403
    [Fact]
    public async Task NotificationPreferences_WorkForEmployersAndAdmins()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var employerClient = _api.CreateClient(employer);
        using var adminClient = await _api.AdminClientAsync();

        foreach (var client in new[] { employerClient, adminClient })
        {
            var get = await client.GetAsync(AccountApi.NotificationsUrl);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal(["applicationStatusChanged", "companyReview", "newApplication"], PropertyNames(await get.Content.ReadAsStringAsync()));
        }

        var put = await employerClient.PutRawJsonAsync(AccountApi.NotificationsUrl,
            "{\"applicationStatusChanged\":true,\"newApplication\":false,\"companyReview\":false}");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(JsonValueKind.False,
            JsonDocument.Parse(await (await employerClient.GetAsync(AccountApi.NotificationsUrl)).Content.ReadAsStringAsync())
                .RootElement.GetProperty("newApplication").ValueKind);
    }

    // Frontend zawsze wysyła pageSize 10, ale zły pageSize/pageNumber nie może dać błędu ani nieograniczonej listy
    [Theory]
    [InlineData("pageNumber=0&pageSize=0", 1, 10)]
    [InlineData("pageNumber=-3&pageSize=-1", 1, 10)]
    [InlineData("pageNumber=1&pageSize=500", 1, 50)]
    [InlineData("", 1, 10)]
    public async Task SavedJobsList_NormalisesPagingParameters(string query, int expectedPage, int expectedSize)
    {
        var user = await _api.RegisterUserAsync("paging");
        using var client = _api.CreateClient(user);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"{AccountApi.SavedJobsUrl}/{(await _api.CreateJobAsync()).Id}", null)).StatusCode);

        var response = await client.GetAsync($"{AccountApi.SavedJobsUrl}?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagination = response.ReadPagination();
        Assert.Equal(expectedPage, pagination.CurrentPage);
        Assert.Equal(expectedSize, pagination.ItemsPerPage);
        Assert.Equal(1, pagination.TotalItems);
    }

    // Ponowne wysłanie linku po zmianie hasła: nowy link działa, a poprzedni (inny security stamp) nie
    [Fact]
    public async Task ResendConfirmation_AfterAPasswordChange_SendsALinkThatWorks()
    {
        var user = await _api.RegisterUserAsync("resend");
        var (_, userId, oldToken) = await _api.Emails.ConfirmationLinkAsync(user.Email);
        using var client = _api.CreateClient(user);
        var change = await client.ChangePasswordAsync(user.Password, NewPassword);
        using var renewed = _api.CreateClient((await change.ReadJsonAsync<UserDto>()).Token);

        Assert.Equal(HttpStatusCode.NoContent, (await renewed.PostAsync("/api/auth/resend-confirmation", null)).StatusCode);

        var email = await _api.Emails.WaitForAsync(user.Email, e => e.Subject == AccountApi.ConfirmationSubject
            && FakeEmailSender.QueryValue(FakeEmailSender.LinkFrom(e, JobBoardFactory.FrontendBaseUrl + "/confirm-email"), "token") != oldToken);
        var newToken = AngularRouterQuery.Parse(RouterUrl(FakeEmailSender.LinkFrom(email, JobBoardFactory.FrontendBaseUrl + "/confirm-email")))["token"];
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.ConfirmEmailAsync(userId, oldToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ConfirmEmailAsync(userId, newToken)).StatusCode);
    }
}
