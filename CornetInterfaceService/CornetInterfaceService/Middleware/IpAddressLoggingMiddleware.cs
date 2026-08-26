using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Context;

namespace CornetInterfaceService.Middleware;

public class IpAddressLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public IpAddressLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Try to get real IP from proxy headers first, then fall back to connection IP
        var xForwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var xRealIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        var localIp = context.Connection.LocalIpAddress?.ToString();

        var ipAddress = xForwardedFor ?? xRealIp ?? remoteIp ?? localIp ?? "localhost";

        // If X-Forwarded-For has multiple IPs, take the first (original client)
        if (ipAddress.Contains(','))
        {
            ipAddress = ipAddress.Split(',')[0].Trim();
        }

        // Convert IPv6 localhost to readable format
        if (ipAddress == "::1") ipAddress = "localhost-ipv6";
        if (ipAddress == "127.0.0.1") ipAddress = "localhost";

        // Add IP address to Serilog log context for all logs in this request
        using (LogContext.PushProperty("ClientIP", ipAddress))
        {
            await _next(context);
        }
    }
}
