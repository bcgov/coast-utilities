using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace CASInterfaceService
{
    public class Program
    {
        private const string URL = "https://<server>:<port>ords/cas/cfs/apinvoice/";
        private const string TokenURL = "https://<server>:<port>/ords/casords/oauth/token";

        public static void Main(string[] args)
        {
            // Load configuration for Serilog setup
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>()
                .Build();

            // Configure Serilog
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("ServiceName", "cas-api")
                .Enrich.WithProperty("ServiceType", "coast-utilities")
                .WriteTo.Console();

            ConfigureSplunk(loggerConfig, config);

            var host = new WebHostBuilder()
                .UseKestrel()
                .UseUrls("http://*:8080")
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureAppConfiguration((hostingContext, configBuilder) =>
                {
                    configBuilder
                        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                        .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables()
                        .AddUserSecrets<Program>();
                })
                .UseStartup<Startup>()
                .Build();

            host.Run();
            Log.CloseAndFlush();
        }

        private static void ConfigureSplunk(LoggerConfiguration loggerConfig, IConfiguration config)
        {
            string splunkUrl = config["SPLUNK_COLLECTOR_URL"];
            string splunkToken = config["SPLUNK_TOKEN"];

            if (!string.IsNullOrEmpty(splunkUrl) && !string.IsNullOrEmpty(splunkToken))
            {
                HttpClientHandler handler = null;

                // In development, accept any SSL certificate
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

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>();
    }

}
