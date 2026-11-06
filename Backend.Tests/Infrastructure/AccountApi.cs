using System.Net;
using System.Net.Http.Json;
using Backend.DTOs;
using Backend.Interfaces;

namespace Backend.Tests.Infrastructure;

// Żądania etapu "konto użytkownika" (profil, dokumenty, usunięcie konta, zapisane oferty) i linki z e-maili
public static class AccountApi
{
    public const string ProfileUrl = "/api/account/profile";
    public const string DocumentsUrl = "/api/account/documents";
    public const string NotificationsUrl = "/api/account/notifications";
    public const string ChangePasswordUrl = "/api/account/change-password";
    public const string SavedJobsUrl = "/api/saved-jobs";

    public const string ConfirmationSubject = "Potwierdź adres e-mail w Job Board";
    public const string ResetSubject = "Zmiana hasła w Job Board";

    public static string DocumentUrl(int id) => $"{DocumentsUrl}/{id}";

    public static string FromProfileUrl(int applicationId) => $"/api/jobapplications/{applicationId}/attachments/from-profile";

    public static Task<HttpResponseMessage> UploadDocumentsAsync(this HttpClient client, params AttachmentUpload[] uploads) =>
        client.PostAsync(DocumentsUrl, AttachmentTestFiles.Form(uploads));

    public static async Task<List<ProfileDocumentDto>> UploadDocumentsOkAsync(this HttpClient client, params AttachmentUpload[] uploads)
    {
        var response = await client.UploadDocumentsAsync(uploads);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Upload failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.ReadJsonAsync<List<ProfileDocumentDto>>();
    }

    public static Task<HttpResponseMessage> AttachFromProfileAsync(this HttpClient client, int applicationId, params int[] documentIds) =>
        client.PostJsonAsync(FromProfileUrl(applicationId), new AttachFromProfileDto { DocumentIds = documentIds.ToList() });

    // DELETE /api/account z JSON-em w treści (jak wysyła Angular)
    public static Task<HttpResponseMessage> DeleteAccountAsync(this HttpClient client, string password) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/account")
        {
            Content = JsonContent.Create(new DeleteAccountDto { Password = password }, options: ApiJson.Options)
        });

    public static Task<HttpResponseMessage> ChangePasswordAsync(this HttpClient client, string current, string next) =>
        client.PostJsonAsync(ChangePasswordUrl, new ChangePasswordDto { CurrentPassword = current, NewPassword = next });

    // Link potwierdzający z ostatniego e-maila do adresu: (userId, token z linku - zakodowany Base64Url)
    public static async Task<(Uri Link, string UserId, string Token)> ConfirmationLinkAsync(this FakeEmailSender emails, string address)
    {
        var email = await emails.WaitForAsync(address, e => e.Subject == ConfirmationSubject);
        var link = FakeEmailSender.LinkFrom(email, JobBoardFactory.FrontendBaseUrl + "/confirm-email");
        return (link, FakeEmailSender.QueryValue(link, "userId"), FakeEmailSender.QueryValue(link, "token"));
    }

    // Link resetu hasła z ostatniego e-maila do adresu: (email, token z linku - zakodowany Base64Url)
    public static async Task<(Uri Link, string Email, string Token)> ResetLinkAsync(this FakeEmailSender emails, string address,
        int expectedCount = 1)
    {
        await emails.WaitForAsync(address, _ => emails.SentTo(address).Count(e => e.Subject == ResetSubject) >= expectedCount);
        var email = emails.SentTo(address).Last(e => e.Subject == ResetSubject);
        var link = FakeEmailSender.LinkFrom(email, JobBoardFactory.FrontendBaseUrl + "/reset-password");
        return (link, FakeEmailSender.QueryValue(link, "email"), FakeEmailSender.QueryValue(link, "token"));
    }

    public static Task<HttpResponseMessage> ConfirmEmailAsync(this HttpClient client, string userId, string token) =>
        client.PostJsonAsync("/api/auth/confirm-email", new ConfirmEmailDto { UserId = userId, Token = token });

    public static Task<HttpResponseMessage> ForgotPasswordAsync(this HttpClient client, string email) =>
        client.PostJsonAsync("/api/auth/forgot-password", new ForgotPasswordDto { Email = email });

    public static Task<HttpResponseMessage> ResetPasswordAsync(this HttpClient client, string email, string token, string newPassword) =>
        client.PostJsonAsync("/api/auth/reset-password", new ResetPasswordDto { Email = email, Token = token, NewPassword = newPassword });

    public static IReadOnlyList<OutgoingEmail> WithSubject(this IEnumerable<OutgoingEmail> emails, string subject) =>
        emails.Where(e => e.Subject == subject).ToList();
}
