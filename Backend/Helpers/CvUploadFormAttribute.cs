using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Backend.Helpers;

// Upload CV: limit rozmiaru żądania i wczytanie formularza przed wiązaniem modelu. Za duże żądanie kończy się
// komunikatem API (400) zamiast ogólnego błędu wiązania formularza. Pliki z formularza powyżej 64 KB są
// buforowane przez ASP.NET Core na dysku (plik tymczasowy), nie w pamięci.
[AttributeUsage(AttributeTargets.Method)]
public sealed class CvUploadFormAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    // przed filtrami wiązania i [Consumes]
    public int Order => int.MinValue;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        // Kestrel: limit całego żądania; TestServer nie obsługuje tej funkcji - wtedy chroni limit formularza
        var bodySizeFeature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = CvFiles.MaxRequestSize;
        }

        if (request.ContentLength > CvFiles.MaxRequestSize)
        {
            context.Result = TooLarge();
            return;
        }

        if (request.HasFormContentType)
        {
            // limity formularza tylko dla tego endpointu: jedna część z plikiem, krótkie nagłówki i granica
            context.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                MultipartBodyLengthLimit = CvFiles.MaxRequestSize,
                BufferBodyLengthLimit = CvFiles.MaxRequestSize,
                MultipartBoundaryLengthLimit = 100,
                MultipartHeadersCountLimit = 8,
                MultipartHeadersLengthLimit = 4 * 1024,
                ValueCountLimit = 16,
                ValueLengthLimit = 4 * 1024,
                KeyLengthLimit = 256
            }));

            try
            {
                await request.ReadFormAsync(context.HttpContext.RequestAborted);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                context.Result = TooLarge();
                return;
            }
            catch (InvalidDataException)
            {
                // uszkodzony multipart albo przekroczony limit części (żądanie bez Content-Length)
                context.Result = new BadRequestObjectResult(new { message = "Invalid multipart form data" });
                return;
            }
        }

        await next();
    }

    private static BadRequestObjectResult TooLarge() => new(new { message = CvFiles.TooLargeMessage });
}
