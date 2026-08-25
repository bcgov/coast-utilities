using System;
using System.Net.Http;
using System.Reflection;
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
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Exceptions;

// Enable Serilog self-diagnostics before any logger is created so sink errors are visible.
Serilog.Debugging.SelfLog.Enable(msg => Console.Error.WriteLine($"[Serilog SelfLog] {msg}"));

// Bootstrap logger captures startup errors before the full Serilog pipeline is ready.
Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://*:8080");

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog(
        (ctx, _, loggerConfiguration) =>
        {
            var hostEnv = ctx.HostingEnvironment;
            var config = ctx.Configuration;

            loggerConfiguration
                .Enrich.FromLogContext()
                .Enrich.WithExceptionDetails()
                .Enrich.WithMachineName()
                .Enrich.WithProperty("ServiceName", "cas-api")
                .Enrich.WithProperty("ServiceType", "coast-utilities")
                .Enrich.WithProperty("environment", hostEnv.EnvironmentName)
                .Enrich.WithEnvironmentUserName()
                .Enrich.WithCorrelationId()
                .Enrich.WithSpan()
                .Enrich.WithProperty(
                    "version",
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown"
                )
                .Enrich.WithProperty("UTC_Timestamp", DateTime.UtcNow.ToString("o"));

            if (hostEnv.IsDevelopment())
                loggerConfiguration.MinimumLevel.Debug();
            else
                loggerConfiguration.MinimumLevel.Information();

            loggerConfiguration
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                // Keep the built-in "Now listening on..."/"Application started..." messages visible
                // despite the blanket Microsoft -> Warning override above.
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Warning);

            loggerConfiguration.WriteTo.Console();

            var splunkCollectorUrl = config["SPLUNK_COLLECTOR_URL"];
            var splunkToken = config["SPLUNK_TOKEN"];

            if (!string.IsNullOrEmpty(splunkCollectorUrl) && !string.IsNullOrEmpty(splunkToken))
            {
                Console.WriteLine($"[Serilog] Splunk sink enabled: {splunkCollectorUrl}");

                HttpClientHandler? handler = null;

                if (hostEnv.IsDevelopment())
                {
                    handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                    };
                }

                loggerConfiguration.WriteTo.EventCollector(
                    splunkHost: splunkCollectorUrl,
                    eventCollectorToken: splunkToken,
                    source: "cas-api",
                    sourceType: "coast:cas-api",
                    host: Environment.MachineName,
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    messageHandler: handler,
                    batchSizeLimit: 100,
                    batchIntervalInSeconds: 2
                );
            }
            else
            {
                Console.WriteLine("[Serilog] Splunk sink NOT configured");
            }
        }
    );

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

    builder.Services.AddControllers().AddNewtonsoftJson();

    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT token (without the 'Bearer ' prefix).",
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme },
                    }
                ] = [],
            });
        });
    }

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

    // Placed before UseAuthentication/UseAuthorization so /hc probes are handled here
    // and never reach the JWT authentication middleware at all.
    app.UseHealthChecks("/hc");

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information(
            "cas-api started successfully. Environment={Environment}, Version={Version}, Urls={Urls}",
            app.Environment.EnvironmentName,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown",
            string.Join(", ", app.Urls)
        );
    });

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
