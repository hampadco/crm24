using System.Text.Json;
using Crm.Infrastructure.Services;

namespace Crm.Web.Middleware;

/// <summary>خطاهای مسیر /api را به ProblemDetails تبدیل می‌کند.</summary>
public class ApiExceptionMiddleware
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly RequestDelegate _next;

    public ApiExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
        {
            if (context.Response.HasStarted)
                throw;

            var (status, title, extra) = Map(ex);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            var body = new Dictionary<string, object?>
            {
                ["type"] = $"https://httpstatuses.com/{status}",
                ["title"] = title,
                ["status"] = status
            };
            if (extra is not null)
            {
                foreach (var kv in extra)
                    body[kv.Key] = kv.Value;
            }

            await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
        }
    }

    private static (int Status, string Title, Dictionary<string, object?>? Extra) Map(Exception ex)
    {
        if (ex is RecordValidationException validation)
        {
            if (validation.Duplicates.Count > 0)
            {
                return (StatusCodes.Status409Conflict, "Duplicate record.", new Dictionary<string, object?>
                {
                    ["errors"] = validation.Errors,
                    ["duplicates"] = validation.Duplicates.Select(d => new
                    {
                        d.FieldName,
                        d.RecordId,
                        d.RecordTitle,
                        d.ModuleName
                    })
                });
            }

            return (StatusCodes.Status422UnprocessableEntity, "Validation failed.", new Dictionary<string, object?>
            {
                ["errors"] = validation.Errors
            });
        }

        if (ex is UnauthorizedAccessException)
            return (StatusCodes.Status403Forbidden, "Forbidden.", null);

        if (ex is KeyNotFoundException)
            return (StatusCodes.Status404NotFound, ex.Message, null);

        if (ex is InvalidOperationException ioe
            && (ioe.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || ioe.Message.Contains("یافت نشد", StringComparison.Ordinal)))
            return (StatusCodes.Status404NotFound, ioe.Message, null);

        return (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", null);
    }
}
