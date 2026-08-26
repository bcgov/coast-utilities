using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using CornetInterfaceService.Authentication;
using CornetInterfaceService.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms;

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
                .Enrich.WithProperty("ServiceName", "cornet-api")
                .Enrich.WithProperty("ServiceType", "coast-utilities")
                .Enrich.WithProperty("environment", hostEnv.EnvironmentName)
                .Enrich.WithEnvironmentUserName()
                .Enrich.WithCorrelationId()
                .Enrich.WithSpan()
                .Enrich.WithProperty(
                    "version",
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown"
                )
                .Enrich.WithProperty("UTC_Timestamp", DateTime.UtcNow.ToString("o"))
                .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ClientIP}] {Message:lj}{NewLine}{Exception}"
        );

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
                    source: "cornet-api",
                    sourceType: "coast:cornet-api",
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

    // Load the YARP reverse proxy configuration from a dedicated file if present
    builder.Configuration.AddJsonFile("yarp.reverseproxy.json", optional: true, reloadOnChange: true);

    builder.Services.AddHttpLogging(logging =>
    {
        logging.CombineLogs = true;
        logging.LoggingFields = HttpLoggingFields.All;
        logging.RequestBodyLogLimit = 8192;
        logging.ResponseBodyLogLimit = 8192;
    });

    builder.Services.AddHealthChecks();

    // Configure Basic and OAuth 2 authentication schemes
    builder.Services
        .AddAuthentication(options =>
        {
            // Default scheme for controllers with [Authorize]
            options.DefaultScheme = "BasicAuthentication";
            options.DefaultChallengeScheme = "BasicAuthentication";
        })
        .AddJwtBearer(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                builder.Configuration.GetSection("jwt").Bind(options);
                Console.WriteLine($"JWT - Authority: {options.Authority}");
                Console.WriteLine($"JWT - Audience: {options.Audience}");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireAudience = true,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(60),
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        // Debug-only: fires on every request (including /hc probes), so it must stay
                        // below the Production minimum level to avoid flooding logs.
                        var hasAuthHeader = !string.IsNullOrWhiteSpace(ctx.Request.Headers["Authorization"]);
                        Log.Debug("JWT - Message received. HasAuthorizationHeader: {HasAuthorizationHeader}", hasAuthHeader);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = ctx =>
                    {
                        var userId =
                            ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? ctx.Principal?.FindFirst("sub")?.Value;
                        Log.Information("JWT - Token validated. UserId: {UserId}", userId);
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        Log.Warning(ctx.Exception, "JWT - Authentication failed.");
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        Log.Warning(
                            "JWT - Challenge. Error: {Error}; Description: {ErrorDescription}",
                            ctx.Error,
                            ctx.ErrorDescription
                        );
                        return Task.CompletedTask;
                    },
                };

                options.Validate();
            }
        )
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BasicAuthenticationHandler>(
            "BasicAuthentication", null);

    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .AddAuthenticationSchemes("BasicAuthentication")
            .RequireAuthenticatedUser()
            .Build();

        options.AddPolicy(
            "JwtBearerPolicy",
            policy =>
            {
                policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser();
            }
        );
    });

    builder.Services.AddControllers().AddNewtonsoftJson();

    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            // Support both Basic and Bearer authentication in Swagger
            options.AddSecurityDefinition("Basic", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "basic",
                In = ParameterLocation.Header,
                Description = "Basic Authentication for controller endpoints.",
            });
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
                        Reference = new OpenApiReference { Id = "Basic", Type = ReferenceType.SecurityScheme },
                    }
                ] = [],
            });
        });
    }

    builder.Services
        .AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
        .AddTransforms(builderContext =>
        {
            // After the incoming JWT is validated, replace the Authorization header
            // on the proxied request with Basic credentials for the downstream API.
            builderContext.AddRequestTransform(transformContext =>
            {
                var username = builder.Configuration["CorVSUDynAPI:Username"];
                var password = builder.Configuration["CorVSUDynAPI:Password"];

                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                    transformContext.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        encoded
                    );
                }

                return ValueTask.CompletedTask;
            });
        });

    var app = builder.Build();
    app.UseMiddleware<IpAddressLoggingMiddleware>();

    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            if (ipAddress == "::1") ipAddress = "localhost-ipv6";

            diagnosticContext.Set("ClientIP", ipAddress);
        };
    });

    app.UseExceptionHandler(appBuilder =>
    {
        appBuilder.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"An unexpected error occurred.\"}");
        });
    });

    app.UseHttpLogging();

    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            var proxyFeature = httpContext.Features.Get<IReverseProxyFeature>();
            if (proxyFeature is not null)
            {
                diagnosticContext.Set("ProxyRouteId", proxyFeature.Route.Config.RouteId);
                diagnosticContext.Set("ProxyClusterId", proxyFeature.Cluster.Config.ClusterId);
                if (proxyFeature.ProxiedDestination is not null)
                    diagnosticContext.Set("ProxyDestinationId", proxyFeature.ProxiedDestination.DestinationId);
            }
        };

        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex != null)
                return LogEventLevel.Error;

            var path = httpContext.Request.Path.ToString();

            if (path.StartsWith("/hc", StringComparison.OrdinalIgnoreCase))
                return httpContext.Response.StatusCode >= 500
                    ? LogEventLevel.Error
                    : LogEventLevel.Verbose;

            if (httpContext.Response.StatusCode == 401)
                return LogEventLevel.Error;

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

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapReverseProxy();

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information(
            "cornet-api started successfully. Environment={Environment}, Version={Version}, Urls={Urls}",
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
