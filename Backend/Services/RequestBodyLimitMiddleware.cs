using Backend.Helpers;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;

namespace Backend.Services;

// Limit rozmiaru treści żądania w jednym miejscu: domyślnie 1 MB (DefaultLimit, także globalny limit Kestrela),
// endpoint z większymi plikami podnosi go atrybutem [RequestSizeLimit(bajty)] (metadane IRequestSizeLimitMetadata).
// Limit trafia do serwera (IHttpMaxRequestBodySizeFeature - Kestrel, IIS), a niezależnie od serwera (TestServer,
// żądanie bez Content-Length) treść jest czytana przez strumień z limitem. Przekroczenie -> BadHttpRequestException
// 413, czyli 413 {message:"Request body too large"} (ClientRequestErrorMiddleware); endpoint może je obsłużyć sam.
// Wymaga dopasowanego endpointu (po routingu), nie czyta treści żądania.
public sealed class RequestBodyLimitMiddleware(RequestDelegate next)
{
    public const long DefaultLimit = 1024 * 1024;

    public Task InvokeAsync(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
        // [DisableRequestSizeLimit] - null, bez limitu
        var limit = metadata == null ? DefaultLimit : metadata.MaxRequestBodySize;

        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = limit;
        }

        if (limit is { } maxBytes && context.Request.ContentLength != 0)
        {
            context.Request.Body = new LengthLimitedStream(context.Request.Body, maxBytes);
        }

        return next(context);
    }
}
