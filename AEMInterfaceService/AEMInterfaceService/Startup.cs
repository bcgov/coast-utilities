using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace AEMInterfaceService
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
                        var audience = Configuration["jwt:Audience"];
                        var authority = Configuration["jwt:Authority"];

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

                                var userId = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? ctx.Principal?.FindFirst("sub")?.Value;

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
                    }
                );

            services.Configure<CookiePolicyOptions>(options =>
            {
                // This lambda determines whether user consent for non-essential cookies is needed for a given request.
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });


            services.AddMvc(opts =>
            {
                opts.EnableEndpointRouting = false;
            }
            ).SetCompatibilityVersion(CompatibilityVersion.Version_3_0);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseCookiePolicy();

            app.UseMvc();
        }
    }
}
