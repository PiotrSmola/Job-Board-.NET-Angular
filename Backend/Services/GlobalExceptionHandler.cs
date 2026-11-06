using Microsoft.AspNetCore.Diagnostics;

namespace Backend.Services;

// Nieobsłużony wyjątek -> 500 z ogólnym komunikatem, bez typu, treści ani stosu wyjątku w odpowiedzi.
// Wyjątek ze stosem loguje middleware UseExceptionHandler (kategoria Microsoft.AspNetCore.Diagnostics, poziom Error);
// przerwane przez klienta żądanie (OperationCanceledException z RequestAborted) middleware obsługuje sam, bez 500.
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public const string Message = "An unexpected error occurred";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(new { message = Message }, cancellationToken);
        return true;
    }
}
