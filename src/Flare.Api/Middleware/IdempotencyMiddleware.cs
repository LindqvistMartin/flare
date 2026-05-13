using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Flare.Api.Middleware;

public sealed class IdempotencyMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private const string HeaderName = "Idempotency-Key";

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!HttpMethods.IsPost(ctx.Request.Method) || !ctx.Request.Headers.TryGetValue(HeaderName, out var keyValues))
        {
            await next(ctx);
            return;
        }

        ctx.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
            body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;

        var cacheKey = ComputeKey(ctx.Request.Method, ctx.Request.Path, keyValues.ToString(), body);

        if (cache.TryGetValue(cacheKey, out CachedResponse? cached) && cached is not null)
        {
            ctx.Response.StatusCode = cached.StatusCode;
            ctx.Response.ContentType = cached.ContentType;
            await ctx.Response.WriteAsync(cached.Body);
            return;
        }

        var originalBody = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try
        {
            await next(ctx);

            buffer.Position = 0;
            string responseBody;
            using (var reader = new StreamReader(buffer, leaveOpen: true))
                responseBody = await reader.ReadToEndAsync();

            cache.Set(cacheKey,
                new CachedResponse(ctx.Response.StatusCode, ctx.Response.ContentType ?? "application/json", responseBody),
                TimeSpan.FromMinutes(5));

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
        }
        finally
        {
            ctx.Response.Body = originalBody;
        }
    }

    private static string ComputeKey(string method, string path, string idempotencyKey, string body)
    {
        var input = $"{method}:{path}:{idempotencyKey}:{body}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private sealed record CachedResponse(int StatusCode, string ContentType, string Body);
}
