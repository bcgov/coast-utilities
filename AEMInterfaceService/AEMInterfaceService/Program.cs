using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

// Bootstrap Serilog before the host builds so startup errors are captured
var bootstrapConfig = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.WithProperty("ServiceName", "aem-api")
    .Enrich.WithProperty("ServiceType", "coast-utilities")
    .WriteTo.Console();

ConfigureSplunk(loggerConfig, bootstrapConfig);

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://*:8080");

    // Configure JWT Authentication
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var audience = builder.Configuration["jwt:Audience"];
            var authority = builder.Configuration["jwt:Authority"];

            options.Authority = authority;
            options.Audience = audience;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                RequireAudience = true,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuer = true,
                ValidIssuer = authority,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(60),
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = async ctx =>
                {
                    await Task.CompletedTask;
                    var hasAuthHeader = !string.IsNullOrWhiteSpace(ctx.Request.Headers["Authorization"]);
                    Console.WriteLine($"JWT - Message received. HasAuthorizationHeader: {hasAuthHeader}");
                },
                OnTokenValidated = async ctx =>
                {
                    await Task.CompletedTask;
                    var userId = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? ctx.Principal?.FindFirst("sub")?.Value;
                    Console.WriteLine($"JWT - Token validated. UserId: {userId}");
                },
                OnAuthenticationFailed = async ctx =>
                {
                    await Task.CompletedTask;
                    Console.WriteLine("JWT - Authentication failed.");
                },
                OnChallenge = async ctx =>
                {
                    await Task.CompletedTask;
                    Console.WriteLine($"JWT - Challenge. Error: {ctx.Error}; Description: {ctx.ErrorDescription}");
                },
            };

            options.Validate();
        });

    builder.Services.AddSerilog();
    builder.Services.AddControllers();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"An unexpected error occurred.\"}");
            });
        });
        app.UseHsts();
    }

    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex != null)
                return LogEventLevel.Error;

            var path = httpContext.Request.Path.ToString();

            if (path.StartsWith("/hc", StringComparison.OrdinalIgnoreCase))
                return httpContext.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : LogEventLevel.Verbose;

            return httpContext.Response.StatusCode >= 400
                ? LogEventLevel.Warning
                : LogEventLevel.Information;
        };
    });

    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

static void ConfigureSplunk(LoggerConfiguration loggerConfig, IConfiguration config)
{
    string splunkUrl = config["SPLUNK_COLLECTOR_URL"];
    string splunkToken = config["SPLUNK_TOKEN"];
    bool isDevelopment = (config["ASPNETCORE_ENVIRONMENT"] ?? "Production")
        .Equals("Development", StringComparison.OrdinalIgnoreCase);

    if (!isDevelopment && !string.IsNullOrEmpty(splunkUrl) && !string.IsNullOrEmpty(splunkToken))
    {
        loggerConfig.WriteTo.EventCollector(
            splunkHost: splunkUrl,
            eventCollectorToken: splunkToken,
            restrictedToMinimumLevel: LogEventLevel.Information,
            source: "aem-api",
            sourceType: "coast:aem-api",
            host: Environment.MachineName);

        Log.Logger = loggerConfig.CreateLogger();
        Log.Information(
            "Serilog configured with Splunk sink at {SplunkUrl} (source: aem-api, sourceType: coast:aem-api)",
            splunkUrl);
    }
    else
    {
        Log.Logger = loggerConfig.CreateLogger();
        Log.Information("Serilog configured with Console sink only (Splunk not configured)");
    }
}
