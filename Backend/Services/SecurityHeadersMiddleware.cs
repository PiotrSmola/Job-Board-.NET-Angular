using Microsoft.AspNetCore.Authorization;
using Microsoft.Net.Http.Headers;

namespace Backend.Services;

// Nagłówki bezpieczeństwa każdej odpowiedzi API (ustawiane tuż przed wysłaniem nagłówków, także dla błędów):
// - X-Content-Type-Options: nosniff - przeglądarka nie zgaduje typu treści,
// - Cache-Control: no-store - dla odpowiedzi z danymi użytkownika: żądanie uwierzytelnione albo endpoint wymagający
//   zalogowania (także jego 401/403); nie nadpisuje Cache-Control ustawionego przez endpoint (np. obrazy firm:
//   "public, max-age=86400" dla firmy zatwierdzonej także przy żądaniu z tokenem, "private, no-store" dla pozostałych -
//   FileDownloads.InlineImageAsync).
// Nagłówki dla strony (CSP, X-Frame-Options...) ustawia nginx, który serwuje frontend.
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";

            if (string.IsNullOrEmpty(headers.CacheControl) && ContainsUserData(context))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }

    private static bool ContainsUserData(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
        || context.Request.Headers.ContainsKey(HeaderNames.Authorization)
        || context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() != null;
}
