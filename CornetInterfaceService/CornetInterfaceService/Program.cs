using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Yarp.ReverseProxy.Transforms;

// Bootstrap Serilog before the host is built so startup errors are captured
var bootstrapConfig = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

ConfigureSerilog(bootstrapConfig);

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://*:8080");

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

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
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
                    OnMessageReceived = async ctx =>
                    {
                        await Task.CompletedTask;
                        var hasAuthHeader = !string.IsNullOrWhiteSpace(ctx.Request.Headers["Authorization"]);
                        Console.WriteLine($"JWT - Message received. HasAuthorizationHeader: {hasAuthHeader}");
                    },
                    OnTokenValidated = async ctx =>
                    {
                        await Task.CompletedTask;
                        var userId =
                            ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
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
                        Console.WriteLine(
                            $"JWT - Challenge. Error: {ctx.Error}; Description: {ctx.ErrorDescription}"
                        );
                    },
                };

                options.Validate();
            }
        );

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(
            JwtBearerDefaults.AuthenticationScheme,
            policy =>
            {
                policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser();
            }
        );
        options.DefaultPolicy = options.GetPolicy(JwtBearerDefaults.AuthenticationScheme) ?? null!;
    });

    builder.Services.AddSerilog();

    builder.Services.AddControllers().AddNewtonsoftJson();

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

    app.UseExceptionHandler(appBuilder =>
    {
        appBuilder.Run(async context =>
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"An unexpected error occurred.\"}");
        });
    });

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

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/hc");
    app.MapReverseProxy();

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

static void ConfigureSerilog(IConfiguration config)
{
    var loggerConfig = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .Enrich.WithExceptionDetails()
        .WriteTo.Console();

    var isDevelopment = (config["ASPNETCORE_ENVIRONMENT"] ?? "Production")
        .Equals("Development", StringComparison.OrdinalIgnoreCase);

    if (
        !isDevelopment
        && !string.IsNullOrEmpty(config["SPLUNK_COLLECTOR_URL"])
        && !string.IsNullOrEmpty(config["SPLUNK_TOKEN"])
    )
    {
        var fields = new Serilog.Sinks.Splunk.CustomFields();
        if (!string.IsNullOrEmpty(config["SPLUNK_CHANNEL"]))
        {
            fields.CustomFieldList.Add(
                new Serilog.Sinks.Splunk.CustomField("channel", config["SPLUNK_CHANNEL"])
            );
        }

        loggerConfig.WriteTo.EventCollector(
            splunkHost: config["SPLUNK_COLLECTOR_URL"],
            sourceType: "cornet-interface",
            eventCollectorToken: config["SPLUNK_TOKEN"],
            restrictedToMinimumLevel: LogEventLevel.Information,
            messageHandler: new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            }
        );

        Log.Logger = loggerConfig.CreateLogger();
        Serilog.Debugging.SelfLog.Enable(Console.Error);
        Log.Logger.Information("Cornet Interface Container Started");
    }
    else
    {
        Log.Logger = loggerConfig.CreateLogger();
    }
}
