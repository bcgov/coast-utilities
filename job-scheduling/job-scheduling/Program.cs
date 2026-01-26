using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog;

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

            // Configure Splunk sink if available
            string splunkUrl = Configuration["SPLUNK_URL"];
            string splunkToken = Configuration["SPLUNK_TOKEN"];

            if (!string.IsNullOrEmpty(splunkUrl) && !string.IsNullOrEmpty(splunkToken))
            {
                HttpClientHandler? handler = null;

                // In development, accept any SSL certificate
                if (Configuration["ASPNETCORE_ENVIRONMENT"] == "Development")
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

            Log.Information("Job execution starting");
            var result = ExecuteJob(Configuration);
            result.Wait();
            Log.Information("Job completed");
            Log.CloseAndFlush();
        }

        static async Task ExecuteJob(IConfigurationRoot Configuration)
        {
            string dynamicsOdataUri = Configuration["DYNAMICS_ODATA_URI"]; // Dynamics ODATA endpoint
            string dynamicsJobName = Configuration["DYNAMICS_JOB_NAME"]; // Dynamics Job Name

            if (string.IsNullOrEmpty(dynamicsOdataUri))
            {
                Log.Error("Configuration setting DYNAMICS_ODATA_URI is blank");
                throw new Exception("Configuration setting DYNAMICS_ODATA_URI is blank.");
            }

            Log.Information("Connecting to Dynamics at {DynamicsUri}", dynamicsOdataUri);

            // Cloud - x.dynamics.com
            string aadTenantId = Configuration["DYNAMICS_AAD_TENANT_ID"]; // Cloud AAD Tenant ID
            string serverAppIdUri = Configuration["DYNAMICS_SERVER_APP_ID_URI"]; // Cloud Server App ID URI
            string appRegistrationClientKey = Configuration["DYNAMICS_APP_REG_CLIENT_KEY"]; // Cloud App Registration Client Key
            string appRegistrationClientId = Configuration["DYNAMICS_APP_REG_CLIENT_ID"]; // Cloud App Registration Client Id

            // One Premise ADFS (2016)
            string adfsOauth2Uri = Configuration["ADFS_OAUTH2_URI"]; // ADFS OAUTH2 URI - usually /adfs/oauth2/token on STS
            string applicationGroupResource = Configuration["DYNAMICS_APP_GROUP_RESOURCE"]; // ADFS 2016 Application Group resource (URI)
            string applicationGroupClientId = Configuration["DYNAMICS_APP_GROUP_CLIENT_ID"]; // ADFS 2016 Application Group Client ID
            string applicationGroupSecret = Configuration["DYNAMICS_APP_GROUP_SECRET"]; // ADFS 2016 Application Group Secret
            string serviceAccountUsername = Configuration["DYNAMICS_USERNAME"]; // Service account username
            string serviceAccountPassword = Configuration["DYNAMICS_PASSWORD"]; // Service account password

            // API Gateway to NTLM user.  This is used in v8 environments.  Note that the SSG Username and password are not the same as the NTLM user.
            string ssgUsername = Configuration["SSG_USERNAME"];  // BASIC authentication username
            string ssgPassword = Configuration["SSG_PASSWORD"];  // BASIC authentication password

            ServiceClientCredentials serviceClientCredentials = null;
            if (!string.IsNullOrEmpty(appRegistrationClientId) && !string.IsNullOrEmpty(appRegistrationClientKey) && !string.IsNullOrEmpty(serverAppIdUri) && !string.IsNullOrEmpty(aadTenantId))
            // Cloud authentication - using an App Registration's client ID, client key.  Add the App Registration to Dynamics as an Application User.
            {
                Log.Information("Using Cloud authentication with App Registration");
                var authenticationContext = new AuthenticationContext(
                "https://login.windows.net/" + aadTenantId);
                ClientCredential clientCredential = new ClientCredential(appRegistrationClientId, appRegistrationClientKey);
                var task = authenticationContext.AcquireTokenAsync(serverAppIdUri, clientCredential);
                task.Wait();
                var authenticationResult = task.Result;
                string token = authenticationResult.CreateAuthorizationHeader().Substring("Bearer ".Length);
                serviceClientCredentials = new TokenCredentials(token);
            }
            if (!string.IsNullOrEmpty(adfsOauth2Uri) &&
                        !string.IsNullOrEmpty(applicationGroupResource) &&
                        !string.IsNullOrEmpty(applicationGroupClientId) &&
                        !string.IsNullOrEmpty(applicationGroupSecret) &&
                        !string.IsNullOrEmpty(serviceAccountUsername) &&
                        !string.IsNullOrEmpty(serviceAccountPassword))
            // ADFS 2016 authentication - using an Application Group Client ID and Secret, plus service account credentials.
            {
                Log.Information("Using ADFS 2016 authentication");
                // create a new HTTP client that is just used to get a token.
                var stsClient = new HttpClient();

                stsClient.DefaultRequestHeaders.Add("client-request-id", Guid.NewGuid().ToString());
                stsClient.DefaultRequestHeaders.Add("return-client-request-id", "true");
                stsClient.DefaultRequestHeaders.Add("Accept", "application/json");

                // Construct the body of the request
                var pairs = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("resource", applicationGroupResource),
                    new KeyValuePair<string, string>("client_id", applicationGroupClientId),
                    new KeyValuePair<string, string>("client_secret", applicationGroupSecret),
                    new KeyValuePair<string, string>("username", serviceAccountUsername),
                    new KeyValuePair<string, string>("password", serviceAccountPassword),
                    new KeyValuePair<string, string>("scope", "openid"),
                    new KeyValuePair<string, string>("response_mode", "form_post"),
                    new KeyValuePair<string, string>("grant_type", "password")
                 };

                // This will also set the content type of the request
                var content = new FormUrlEncodedContent(pairs);
                // send the request to the ADFS server
                var stsResponse = stsClient.PostAsync(adfsOauth2Uri, content).GetAwaiter().GetResult();
                var stsResponseContent = stsResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                // response should be in JSON format.
                try
                {
                    Dictionary<string, string> result = JsonConvert.DeserializeObject<Dictionary<string, string>>(stsResponseContent);
                    string token = result["access_token"];
                    // set the bearer token.
                    serviceClientCredentials = new TokenCredentials(token);
                    var authorization = $"Bearer {token}";

                    // Code to perform Scheduled task
                    var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("x-client-SKU", "PCL.CoreCLR");
                    client.DefaultRequestHeaders.Add("x-client-Ver", "5.1.0.0");
                    client.DefaultRequestHeaders.Add("x-ms-PKeyAuth", "1.0");
                    client.DefaultRequestHeaders.Add("client-request-id", Guid.NewGuid().ToString());
                    client.DefaultRequestHeaders.Add("return-client-request-id", "true");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");
                    client.DefaultRequestHeaders.Add("Authorization", authorization);
                    client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                    client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                    client.DefaultRequestHeaders.Add("Accept", "application/json");

                    string url = dynamicsOdataUri + dynamicsJobName;
                    Log.Information("Executing Dynamics job: {JobName} at {Url}", dynamicsJobName, url);

                    HttpRequestMessage _httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                    _httpRequest.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                    var dynamicsResults = await client.SendAsync(_httpRequest);
                    HttpStatusCode _statusCode = dynamicsResults.StatusCode;

                    var responseString = dynamicsResults.ToString();
                    var responseContent = await dynamicsResults.Content.ReadAsStringAsync();

                    Log.Information("Dynamics response: {Response}", responseString);
                    Log.Information("Dynamics response content: {Content}", responseContent);

                    // we need to fail job if we don't get a 200 response from Dynamics
                    if (dynamicsResults.StatusCode != HttpStatusCode.OK)
                    {
                        Log.Error("Error calling Dynamics job. Status: {StatusCode}, Content: {Content}", dynamicsResults.StatusCode, responseContent);
                    }
                    else
                    {
                        Log.Information("Dynamics job completed successfully with status {StatusCode}", dynamicsResults.StatusCode);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error during ADFS authentication or job execution. STS Response: {StsResponse}", stsResponseContent);
                    throw new Exception(e.Message + " " + stsResponseContent);
                }
            }
            else if (!string.IsNullOrEmpty(ssgUsername) && !string.IsNullOrEmpty(ssgPassword))
            // Authenticate using BASIC authentication - used for API Gateways with BASIC authentication.  Add the NTLM user associated with the API gateway entry to Dynamics as a user.            
            {
                Log.Information("Using BASIC authentication with SSG");
                serviceClientCredentials = new BasicAuthenticationCredentials()
                {
                    UserName = ssgUsername,
                    Password = ssgPassword
                };
            }
            else
            {
                Log.Error("No configured connection to Dynamics");
                throw new Exception("No configured connection to Dynamics.");
            }
        }
    }
}
