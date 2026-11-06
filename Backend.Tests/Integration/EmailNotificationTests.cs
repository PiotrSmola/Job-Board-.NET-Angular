using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Powiadomienia e-mail wg preferencji: kandydat - złożenie aplikacji i zmiana statusu, członkowie firmy - nowa
// aplikacja i decyzja admina o firmie; e-maile powstają w outboksie razem ze zdarzeniem. Powiadomienia idą tylko na
// potwierdzone adresy, więc scenariusze używają kont z potwierdzonym e-mailem.
[Collection(ApiCollection.Name)]
public class EmailNotificationTests
{
    private readonly ApiFixture _api;

    public EmailNotificationTests(ApiFixture api) => _api = api;

    private async Task SetPreferencesAsync(TestUser user, bool applicationStatusChanged, bool newApplication, bool companyReview)
    {
        using var client = _api.CreateClient(user);
        var response = await client.PutJsonAsync(AccountApi.NotificationsUrl, new NotificationPreferencesDto
        {
            ApplicationStatusChanged = applicationStatusChanged,
            NewApplication = newApplication,
            CompanyReview = companyReview
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<TestUser> ConfirmedUserAsync()
    {
        var user = await _api.RegisterUserAsync("notify");
        await _api.MarkEmailConfirmedAsync(user);
        return user;
    }

    // pracodawca z potwierdzonym adresem; approved - firma od razu zatwierdzona przez admina
    private async Task<TestEmployer> ConfirmedEmployerAsync(bool approved)
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.MarkEmailConfirmedAsync(employer.User);
        if (approved)
        {
            await _api.ApproveCompanyAsync(employer.CompanyId);
        }

        return employer;
    }

    [Fact]
    public async Task Apply_NotifiesTheCandidateAndTheCompanyMembersWithoutCandidateData()
    {
        var employer = await ConfirmedEmployerAsync(approved: true);
        var job = await _api.CreateEmployerJobAsync(employer, j => j.Title = "Angular <Developer>");
        var candidate = await ConfirmedUserAsync();

        await _api.ApplyAsync(candidate, job.Id);

        var confirmation = await _api.Emails.WaitForAsync(candidate.Email, e => e.Subject.StartsWith("Aplikacja złożona"));
        Assert.Equal("Aplikacja złożona: Angular <Developer>", confirmation.Subject);
        Assert.Contains($"„Angular <Developer>” w firmie {employer.CompanyName}", confirmation.TextBody);
        Assert.Contains("„Angular &lt;Developer&gt;”", confirmation.HtmlBody);
        Assert.Contains($"{JobBoardFactory.FrontendBaseUrl}/my-applications", confirmation.TextBody);

        var newApplication = await _api.Emails.WaitForAsync(employer.Email, e => e.Subject.StartsWith("Nowa aplikacja"));
        Assert.DoesNotContain(candidate.Email, newApplication.TextBody + newApplication.HtmlBody);
        Assert.Contains($"{JobBoardFactory.FrontendBaseUrl}/employer", newApplication.TextBody);
    }

    [Fact]
    public async Task ReapplyAfterWithdrawal_NotifiesAgain()
    {
        var employer = await ConfirmedEmployerAsync(approved: true);
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await ConfirmedUserAsync();
        var application = await _api.ApplyAsync(candidate, job.Id);
        await _api.Emails.WaitForAsync(candidate.Email, e => e.Subject.StartsWith("Aplikacja złożona"));
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(candidate, application.Id)).StatusCode);

        await _api.ApplyAsync(candidate, job.Id);

        await _api.Emails.WaitForAsync(candidate.Email,
            _ => _api.Emails.SentTo(candidate.Email).Count(e => e.Subject.StartsWith("Aplikacja złożona")) == 2);
        await _api.Emails.WaitForAsync(employer.Email,
            _ => _api.Emails.SentTo(employer.Email).Count(e => e.Subject.StartsWith("Nowa aplikacja")) == 2);
    }

    [Fact]
    public async Task StatusChange_NotifiesTheCandidateWithTheCommentButANoteAloneDoesNot()
    {
        var employer = await ConfirmedEmployerAsync(approved: true);
        var job = await _api.CreateEmployerJobAsync(employer, j => j.Title = "QA Engineer");
        var candidate = await ConfirmedUserAsync();
        var application = await _api.ApplyAsync(candidate, job.Id);
        using var recruiter = _api.CreateClient(employer);

        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.ChangeApplicationStatusAsync(
            recruiter, application.Id, ApplicationStatus.Interview, "Zapraszamy w <b>piątek</b>")).StatusCode);

        var email = await _api.Emails.WaitForAsync(candidate.Email, e => e.Subject == "Zmiana statusu aplikacji: QA Engineer");
        Assert.Contains("zmienił się na: Rozmowa", email.TextBody);
        Assert.Contains("> Zapraszamy w <b>piątek</b>", email.TextBody);
        Assert.Contains("Zapraszamy w &lt;b&gt;piątek&lt;/b&gt;", email.HtmlBody);

        // notatka bez zmiany statusu - bez e-maila
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.ChangeApplicationStatusAsync(
            recruiter, application.Id, ApplicationStatus.Interview, "Notatka")).StatusCode);
        var statusEmails = _api.Emails.SentTo(candidate.Email).Count(e => e.Subject.StartsWith("Zmiana statusu"));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(statusEmails, _api.Emails.SentTo(candidate.Email).Count(e => e.Subject.StartsWith("Zmiana statusu")));
        Assert.Equal(1, await _api.QueryDbAsync(db =>
            db.EmailMessages.CountAsync(m => m.UserId == candidate.Id && m.Type == EmailType.ApplicationStatusChanged)));
    }

    [Fact]
    public async Task DisabledPreferences_StopTheNotificationsButNotTheSecurityEmails()
    {
        var employer = await ConfirmedEmployerAsync(approved: true);
        await SetPreferencesAsync(employer.User, applicationStatusChanged: true, newApplication: false, companyReview: true);
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await ConfirmedUserAsync();
        await SetPreferencesAsync(candidate, applicationStatusChanged: false, newApplication: true, companyReview: true);

        var application = await _api.ApplyAsync(candidate, job.Id);
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview);
        using var anonymous = _api.CreateClient();
        await anonymous.ForgotPasswordAsync(candidate.Email);

        // e-mail bezpieczeństwa wysłany mimo wyłączonych powiadomień
        await _api.Emails.ResetLinkAsync(candidate.Email);
        var notifications = await _api.QueryDbAsync(db => db.EmailMessages.CountAsync(m =>
            (m.UserId == candidate.Id || m.UserId == employer.Id)
            && m.Type != EmailType.EmailConfirmation && m.Type != EmailType.PasswordReset && m.Type != EmailType.CompanyReviewed));
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task CompanyDecisions_NotifyTheMembers()
    {
        var employer = await ConfirmedEmployerAsync(approved: false);

        await _api.RejectCompanyAsync(employer.CompanyId, "Brak strony <www>");
        var rejected = await _api.Emails.WaitForAsync(employer.Email, e => e.Subject.EndsWith("nie została zatwierdzona"));
        Assert.Equal($"Firma {employer.CompanyName} nie została zatwierdzona", rejected.Subject);
        Assert.Contains("> Brak strony <www>", rejected.TextBody);
        Assert.Contains("Brak strony &lt;www&gt;", rejected.HtmlBody);

        await _api.ApproveCompanyAsync(employer.CompanyId);
        var approved = await _api.Emails.WaitForAsync(employer.Email, e => e.Subject.EndsWith("została zatwierdzona") && !e.Subject.Contains("nie "));
        Assert.Contains("Możesz teraz publikować oferty pracy", approved.TextBody);

        // ponowne zatwierdzenie zatwierdzonej firmy nie wysyła kolejnego e-maila
        await _api.ApproveCompanyAsync(employer.CompanyId);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(2, await _api.QueryDbAsync(db =>
            db.EmailMessages.CountAsync(m => m.UserId == employer.Id && m.Type == EmailType.CompanyReviewed)));
    }

    [Fact]
    public async Task CompanyDecision_WithTheNotificationDisabled_SendsNothing()
    {
        var employer = await ConfirmedEmployerAsync(approved: false);
        await SetPreferencesAsync(employer.User, applicationStatusChanged: true, newApplication: true, companyReview: false);

        await _api.ApproveCompanyAsync(employer.CompanyId);

        Assert.False(await _api.QueryDbAsync(db =>
            db.EmailMessages.AnyAsync(m => m.UserId == employer.Id && m.Type == EmailType.CompanyReviewed)));
    }

    // niepotwierdzony adres mógł zostać wpisany cudzy - powiadomienia (wszystkie cztery rodzaje) idą dopiero po
    // potwierdzeniu, e-maile bezpieczeństwa (potwierdzenie, reset hasła) od razu
    [Fact]
    public async Task UnconfirmedAddresses_GetOnlyTheSecurityEmails()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.ApproveCompanyAsync(employer.CompanyId);
        var job = await _api.CreateEmployerJobAsync(employer, j => j.Title = "Unconfirmed Notifications");
        var candidate = await _api.RegisterUserAsync("notify");

        var application = await _api.ApplyAsync(candidate, job.Id);
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview);
        using var anonymous = _api.CreateClient();
        await anonymous.ForgotPasswordAsync(candidate.Email);

        await _api.Emails.ConfirmationLinkAsync(candidate.Email);
        await _api.Emails.ConfirmationLinkAsync(employer.Email);
        await _api.Emails.ResetLinkAsync(candidate.Email);
        Assert.False(await _api.QueryDbAsync(db => db.EmailMessages.AnyAsync(m =>
            (m.UserId == candidate.Id || m.UserId == employer.Id)
            && m.Type != EmailType.EmailConfirmation && m.Type != EmailType.PasswordReset)));

        // po potwierdzeniu adresu kandydat dostaje kolejne powiadomienie, pracodawca nadal nie
        await _api.MarkEmailConfirmedAsync(candidate);
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Interview);

        var email = await _api.Emails.WaitForAsync(candidate.Email,
            e => e.Subject == "Zmiana statusu aplikacji: Unconfirmed Notifications");
        Assert.Contains("zmienił się na: Rozmowa", email.TextBody);
        Assert.Equal(1, await _api.QueryDbAsync(db => db.EmailMessages.CountAsync(m =>
            (m.UserId == candidate.Id || m.UserId == employer.Id)
            && m.Type != EmailType.EmailConfirmation && m.Type != EmailType.PasswordReset)));
    }
}
