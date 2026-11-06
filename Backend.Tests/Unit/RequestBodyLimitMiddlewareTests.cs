using Backend.Helpers;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Tests.Unit;

// Limit treści żądania w jednym miejscu: domyślnie 1 MB, [RequestSizeLimit] / [DisableRequestSizeLimit] endpointu
// zmieniają go - zarówno dla serwera (IHttpMaxRequestBodySizeFeature), jak i niezależnie od niego (strumień z limitem)
public class RequestBodyLimitMiddlewareTests
{
    private sealed class BodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; init; }
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }

    private static DefaultHttpContext Context(long bodySize, params object[] endpointMetadata)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(new byte[bodySize]);
        context.Request.ContentLength = bodySize;
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(endpointMetadata), "test"));
        return context;
    }

    // przekazuje żądanie dalej do "endpointu", który czyta całą treść
    private static async Task<long> RunAsync(HttpContext context)
    {
        long read = 0;
        var middleware = new RequestBodyLimitMiddleware(async ctx =>
        {
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = await ctx.Request.Body.ReadAsync(buffer)) > 0)
            {
                read += count;
            }
        });
        await middleware.InvokeAsync(context);
        return read;
    }

    [Fact]
    public async Task EndpointWithoutAttribute_GetsTheOneMegabyteDefault()
    {
        var feature = new BodySizeFeature();
        var context = Context(RequestBodyLimitMiddleware.DefaultLimit);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        Assert.Equal(RequestBodyLimitMiddleware.DefaultLimit, await RunAsync(context));
        Assert.Equal(1024 * 1024, feature.MaxRequestBodySize);
    }

    [Fact]
    public async Task BodyOverTheDefault_ThrowsA413BadRequestEvenWithoutServerSupport()
    {
        var context = Context(RequestBodyLimitMiddleware.DefaultLimit + 1);

        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(() => RunAsync(context));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
    }

    [Fact]
    public async Task RequestSizeLimitAttribute_RaisesTheLimitForItsEndpoint()
    {
        var feature = new BodySizeFeature();
        var context = Context(5 * 1024 * 1024, new RequestSizeLimitAttribute(AttachmentFiles.MaxRequestSize));
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        Assert.Equal(5 * 1024 * 1024, await RunAsync(context));
        Assert.Equal(AttachmentFiles.MaxRequestSize, feature.MaxRequestBodySize);

        var tooLarge = Context(AttachmentFiles.MaxRequestSize + 1, new RequestSizeLimitAttribute(AttachmentFiles.MaxRequestSize));
        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(() => RunAsync(tooLarge));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
    }

    [Fact]
    public async Task DisableRequestSizeLimit_RemovesTheLimit()
    {
        var feature = new BodySizeFeature();
        var context = Context(RequestBodyLimitMiddleware.DefaultLimit * 2, new DisableRequestSizeLimitAttribute());
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        Assert.Equal(RequestBodyLimitMiddleware.DefaultLimit * 2, await RunAsync(context));
        Assert.Null(feature.MaxRequestBodySize);
    }

    [Fact]
    public async Task ReadOnlyServerLimit_IsLeftAlone()
    {
        var feature = new BodySizeFeature { IsReadOnly = true };
        var context = Context(10);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        Assert.Equal(10, await RunAsync(context));
        Assert.Equal(30_000_000, feature.MaxRequestBodySize);
    }
}
