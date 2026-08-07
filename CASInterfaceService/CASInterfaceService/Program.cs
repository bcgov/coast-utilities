using System;
using System.Net.Http;
using System.Threading.Tasks;
using CASInterfaceService.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

// Bootstrap config and Serilog before the host builds so startup errors are captured
var bootstrapConfig = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.WithProperty("ServiceName", "cas-api")
    .Enrich.WithProperty("ServiceType", "coast-utilities")
    .WriteTo.Console();

ConfigureSplunk(loggerConfig, bootstrapConfig);

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://*:8080");

    // HTTP request/response logging
    builder.Services.AddHttpLogging(logging =>
    {
        logging.CombineLogs = true;
        logging.LoggingFields = HttpLoggingFields.All;
        logging.RequestBodyLogLimit = 8192;
        logging.ResponseBodyLogLimit = 8192;
    });

    builder.Services.AddHealthChecks();

    // JWT Authentication
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["auth:jwt:authority"];
            options.Audience = builder.Configuration["auth:jwt:audience"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var sub = context.Principal?.FindFirst("sub")?.Value ?? "unknown";
                    Log.Information("JWT token validated for subject {Subject}", sub);
                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    Log.Warning("JWT authentication failed: {Error}", context.Exception.Message);
                    return Task.CompletedTask;
                }
            };
            options.Validate();
        });

    builder.Services.AddSingleton<IAuthorizationHandler, ConditionalAuthorizationHandler>();

    builder.Services.AddAuthorization(options =>
    {
        var conditionalPolicy = new AuthorizationPolicyBuilder()
            .AddRequirements(new ConditionalAuthorizationRequirement())
            .Build();

        options.DefaultPolicy = conditionalPolicy;
        options.FallbackPolicy = conditionalPolicy;
    });

    builder.Services.AddSerilog();
    builder.Services.AddControllers().AddNewtonsoftJson();

    var app = builder.Build();

    // Exception handling must be outermost in the pipeline
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
    app.MapHealthChecks("/hc");

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

    if (!string.IsNullOrEmpty(splunkUrl) && !string.IsNullOrEmpty(splunkToken))
    {
        HttpClientHandler handler = null;

        if (config["ASPNETCORE_ENVIRONMENT"] == "Development")
        {
            Log.Debug("Development environment detected - accepting any SSL certificate for Splunk");
            handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };
        }

        loggerConfig.WriteTo.EventCollector(
            splunkHost: splunkUrl,
            eventCollectorToken: splunkToken,
            restrictedToMinimumLevel: LogEventLevel.Information,
            source: "cas-api",
            sourceType: "coast:cas-api",
            host: Environment.MachineName,
            messageHandler: handler);

        Log.Logger = loggerConfig.CreateLogger();
        Log.Information("Serilog configured with Splunk sink at {SplunkUrl} (source: cas-api, sourceType: coast:cas-api)",
            splunkUrl);
    }
    else
    {
        Log.Logger = loggerConfig.CreateLogger();
        Log.Information("Serilog configured with Console sink only (Splunk not configured)");
    }
}
