namespace Backend.Services;

// Błąd klienta przy odczycie żądania (BadHttpRequestException - np. ciało ponad limit, zbyt wolne wysyłanie,
// uszkodzone żądanie) -> jego kod statusu i ogólny komunikat (GlobalExceptionHandler.WriteClientErrorAsync), log
// Warning bez stosu. Umieszczony zaraz za UseExceptionHandler, bo ten loguje każdy wyjątek jako Error, zanim
// przekaże go do IExceptionHandler. Gdy odpowiedź już się zaczęła albo klient zerwał połączenie, wyjątek idzie dalej.
public sealed class ClientRequestErrorMiddleware(RequestDelegate next, ILogger<ClientRequestErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            var status = GlobalExceptionHandler.ClientErrorStatus(ex);
            logger.LogWarning("Rejected {Method} {Path} with {StatusCode}: {Reason}",
                context.Request.Method, context.Request.Path, status, ex.Message);

            // bez Response.Clear() - nagłówki ustawione wcześniej w potoku (np. CORS) zostają
            await GlobalExceptionHandler.WriteClientErrorAsync(context, ex, context.RequestAborted);
        }
    }
}
