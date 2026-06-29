using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using CASInterfaceService.Authorization;
using Microsoft.AspNetCore.Authorization;
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

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = Configuration["auth:jwt:authority"];
                    options.Audience = Configuration["auth:jwt:audience"];
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

            services.AddSingleton<IAuthorizationHandler, ConditionalAuthorizationHandler>();

            services.AddAuthorization(options =>
            {
                var conditionalPolicy = new AuthorizationPolicyBuilder()
                    .AddRequirements(new ConditionalAuthorizationRequirement())
                    .Build();

                options.DefaultPolicy = conditionalPolicy;
                options.FallbackPolicy = conditionalPolicy;
            });

            services.AddSerilog();

            services.AddMvc(opts =>
            {
                opts.EnableEndpointRouting = false;
            }).SetCompatibilityVersion(CompatibilityVersion.Version_3_0)
              .AddNewtonsoftJson();
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
            });

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

            app.UseMvc();
        }
    }
}
