using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Exceptions;

namespace CASInterfaceService
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Configure JWT Authentication
            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        Configuration.GetSection("jwt").Bind(options);
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

            // Configure Authorization
            services.AddAuthorization(options =>
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
            services.AddHttpLogging(logging =>
            {
                logging.CombineLogs = true;
                logging.LoggingFields = HttpLoggingFields.All;
                logging.RequestBodyLogLimit = 8192;
                logging.ResponseBodyLogLimit = 8192;
            });

            services.Configure<CookiePolicyOptions>(options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddHealthChecks();

            services.AddSerilog();

            services.AddMvc();

            services.AddReverseProxy().LoadFromConfig(Configuration.GetSection("ReverseProxy"));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseSerilogRequestLogging(options =>
            {
                options.GetLevel = (httpContext, elapsed, ex) =>
                {
                    if (ex != null)
                        return Serilog.Events.LogEventLevel.Error;

                    var path = httpContext.Request.Path.ToString();

                    if (path.StartsWith("/hc", StringComparison.OrdinalIgnoreCase))
                        return httpContext.Response.StatusCode >= 500
                            ? Serilog.Events.LogEventLevel.Error
                            : Serilog.Events.LogEventLevel.Verbose;

                    return httpContext.Response.StatusCode >= 400
                        ? Serilog.Events.LogEventLevel.Warning
                        : Serilog.Events.LogEventLevel.Information;
                };
            });

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/hc");
                endpoints.MapReverseProxy();
            });

            if (
                !string.IsNullOrEmpty(Configuration["SPLUNK_COLLECTOR_URL"])
                && !string.IsNullOrEmpty(Configuration["SPLUNK_TOKEN"])
            )
            {
                Serilog.Sinks.Splunk.CustomFields fields = new Serilog.Sinks.Splunk.CustomFields();
                if (!string.IsNullOrEmpty(Configuration["SPLUNK_CHANNEL"]))
                {
                    fields.CustomFieldList.Add(
                        new Serilog.Sinks.Splunk.CustomField("channel", Configuration["SPLUNK_CHANNEL"])
                    );
                }
                var splunkUri = new Uri(Configuration["SPLUNK_COLLECTOR_URL"]);
                var upperSplunkHost = splunkUri.Host?.ToUpperInvariant() ?? string.Empty;

                // Fix for bad SSL issues

                Log.Logger = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .Enrich.WithExceptionDetails()
                    .WriteTo.Console()
                    .WriteTo.EventCollector(
                        splunkHost: Configuration["SPLUNK_COLLECTOR_URL"],
                        sourceType: "cornet-interface",
                        eventCollectorToken: Configuration["SPLUNK_TOKEN"],
                        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
#pragma warning disable CA2000 // Dispose objects before losing scope
                        messageHandler: new HttpClientHandler()
                        {
                            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                            {
                                return true;
                            },
                        }
#pragma warning restore CA2000 // Dispose objects before losing scope
                    )
                    .CreateLogger();

                Serilog.Debugging.SelfLog.Enable(Console.Error);

                Log.Logger.Information("Cornet Interface Container Started");
            }
            else
            {
                Log.Logger = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .Enrich.WithExceptionDetails()
                    .WriteTo.Console()
                    .CreateLogger();
            }

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();
        }
    }
}
