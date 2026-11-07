namespace Upcheck.Api.Common;

/// <summary>
/// Guards mutating endpoints with a shared API key. If no key is configured (the development
/// default) the endpoints are open; once <c>ApiKey</c> is set, callers must send a matching
/// <c>X-Api-Key</c> header.
/// </summary>
public sealed class ApiKeyFilter(IConfiguration config) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = config["ApiKey"];
        if (!string.IsNullOrEmpty(configured))
        {
            var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
            if (!string.Equals(provided, configured, StringComparison.Ordinal))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid or missing API key");
            }
        }

        return await next(context);
    }
}
