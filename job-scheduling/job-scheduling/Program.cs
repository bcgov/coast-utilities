using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;
using Shared.Database;
using Microsoft.Extensions.DependencyInjection;

namespace job_scheduling
{
    class Program
    {
        static void Main(string[] args)
        {
            // Load configuration for Serilog setup
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>();
            var Configuration = builder.Build();

            // Configure Serilog
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("ServiceName", "job-scheduler")
                .Enrich.WithProperty("ServiceType", "coast-utilities")
                .WriteTo.Console();

            ConfigureSplunk(loggerConfig, Configuration);

            Log.Information("Job execution starting");
            var result = ExecuteJob(Configuration);
            result.Wait();
            Log.Information("Job completed");
            Log.CloseAndFlush();
        }

        private static void ConfigureSplunk(LoggerConfiguration loggerConfig, IConfigurationRoot config)
        {
            string splunkUrl = config["SPLUNK_COLLECTOR_URL"];
            string splunkToken = config["SPLUNK_TOKEN"];

            if (!string.IsNullOrEmpty(splunkUrl) && !string.IsNullOrEmpty(splunkToken))
            {
                HttpClientHandler? handler = null;

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
                    source: "job-scheduler",
                    sourceType: "coast:job-scheduler",
                    host: Environment.MachineName,
                    messageHandler: handler);

                Log.Logger = loggerConfig.CreateLogger();
                Log.Information("Serilog configured with Splunk sink at {SplunkUrl} (source: job-scheduler, sourceType: coast:job-scheduler)",
                    splunkUrl);
            }
            else
            {
                Log.Logger = loggerConfig.CreateLogger();
                Log.Information("Serilog configured with Console sink only (Splunk not configured)");
            }
        }

        static async Task ExecuteJob(IConfigurationRoot Configuration)
        {
            // Bind Dynamics configuration
            var dynamicsOptions =
                Configuration.GetSection("Dynamics").Get<DynamicsTokenProviderOptions>()
                ?? new DynamicsTokenProviderOptions();

            string dynamicsOdataUri = dynamicsOptions.DynamicsApiEndpointUrl;
            string dynamicsJobName = Configuration["DYNAMICS_JOB_NAME"];

            if (string.IsNullOrEmpty(dynamicsOdataUri))
            {
                Log.Error("Configuration setting Dynamics:DynamicsApiEndpointUrl is blank");
                throw new Exception("Configuration setting Dynamics:DynamicsApiEndpointUrl is blank.");
            }

            if (string.IsNullOrEmpty(dynamicsJobName))
            {
                Log.Error("Configuration setting DYNAMICS_JOB_NAME is blank");
                throw new Exception("Configuration setting DYNAMICS_JOB_NAME is blank.");
            }

            Log.Information("Connecting to Dynamics at {DynamicsUri}", dynamicsOdataUri);

            // Setup dependency injection
            var services = new ServiceCollection();
            services.AddHttpClient();
            var serviceProvider = services.BuildServiceProvider();
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // Determine authentication type and create token provider
            ITokenProvider tokenProvider = DynamicsAuthHelper.CreateTokenProvider(Configuration, httpClientFactory);

            try
            {
                // Acquire token
                string token = await tokenProvider.AcquireToken();

                // Call Dynamics job
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                string url = dynamicsOdataUri + dynamicsJobName;
                Log.Information("Executing Dynamics job: {JobName} at {Url}", dynamicsJobName, url);

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                httpRequest.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                var response = await client.SendAsync(httpRequest);
                var responseContent = await response.Content.ReadAsStringAsync();

                Log.Information("Dynamics response status: {StatusCode}", response.StatusCode);
                Log.Debug("Dynamics response content: {Content}", responseContent);

                // Check if successful
                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent)
                {
                    Log.Information("Dynamics job completed successfully");
                }
                else
                {
                    Log.Error("Dynamics job failed with status {StatusCode}: {Content}", 
                        response.StatusCode, responseContent);
                    throw new Exception($"Dynamics job failed with status {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing Dynamics job");
                throw;
            }
        }
    }
}
