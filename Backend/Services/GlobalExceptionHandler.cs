using Microsoft.AspNetCore.Diagnostics;

namespace Backend.Services;

// Nieobsłużony wyjątek -> 500 z ogólnym komunikatem, bez typu, treści ani stosu wyjątku w odpowiedzi.
// Wyjątek ze stosem loguje middleware UseExceptionHandler (kategoria Microsoft.AspNetCore.Diagnostics, poziom Error);
// przerwane przez klienta żądanie (OperationCanceledException z RequestAborted) middleware obsługuje sam, bez 500.
// Błąd klienta przy odczycie żądania (BadHttpRequestException: za duże ciało, przekroczony czas, uszkodzone żądanie)
// to nie awaria serwera - dostaje swój kod statusu; zwykle przechwytuje go wcześniej ClientRequestErrorMiddleware
// (log Warning), tu trafia tylko z middleware'ów przed nim.
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public const string Message = "An unexpected error occurred";
    public const string RequestTooLargeMessage = "Request body too large";
    public const string RequestTimeoutMessage = "Request timed out";
    public const string BadRequestMessage = "Invalid request";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            await WriteClientErrorAsync(httpContext, badRequest, cancellationToken);
            return true;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(new { message = Message }, cancellationToken);
        return true;
    }

    // Kod statusu z wyjątku (tylko 4xx, inaczej 400) i ogólny komunikat - treść wyjątku (np. limit w bajtach)
    // nie trafia do odpowiedzi
    public static Task WriteClientErrorAsync(HttpContext httpContext, BadHttpRequestException exception,
        CancellationToken cancellationToken)
    {
        var status = ClientErrorStatus(exception);
        httpContext.Response.StatusCode = status;
        return httpContext.Response.WriteAsJsonAsync(new { message = ClientErrorMessage(status) }, cancellationToken);
    }

    public static int ClientErrorStatus(BadHttpRequestException exception) =>
        exception.StatusCode is >= 400 and < 500 ? exception.StatusCode : StatusCodes.Status400BadRequest;

    private static string ClientErrorMessage(int status) => status switch
    {
        StatusCodes.Status413PayloadTooLarge => RequestTooLargeMessage,
        StatusCodes.Status408RequestTimeout => RequestTimeoutMessage,
        _ => BadRequestMessage
    };
}
