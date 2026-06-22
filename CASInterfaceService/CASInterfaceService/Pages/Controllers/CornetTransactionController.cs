using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using CASInterfaceService.Pages.Models;
using Gov.Cscp.VictimServices.Public.JsonObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CASInterfaceService.Pages.Models.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Shared.Database;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace CASInterfaceService.Pages.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CornetTransactionController : Controller
    {
        private readonly IConfiguration _configuration;

        public CornetTransactionController(IConfiguration configuration)//, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
        }

        [HttpPost]
        public CornetTransactionRegistrationReply RegisterCornetTransaction(CornetTransaction cornetTransaction)
        {
            Log.Information("RegisterCornetTransaction called");
            CornetTransactionRegistrationReply cornetregreply = new CornetTransactionRegistrationReply();
            CornetTransactionRegistration.getInstance().Add(cornetTransaction);
            Log.Debug("Cornet transaction data received and registered");

            var t = Task.Run(() => CallDynamicsWithCornetData(_configuration, cornetTransaction));
            t.Wait();
            Log.Debug("Dynamics call completed");

            if (t.Result.Contains("Cornet Notification "))
            {
                cornetregreply.ResponseCode = "200";
                cornetregreply.ResponseMessage = "Success";
                Log.Information("RegisterCornetTransaction succeeded");
            }
            else
            {
                cornetregreply.ResponseMessage = "Failure";
                cornetregreply.ResponseCode = t.Result;
                Log.Warning("RegisterCornetTransaction failed. Dynamics response: {DynamicsResult}", t.Result);
            }

            Log.Debug("Exiting RegisterCornetTransaction");
            return cornetregreply;

        }

        private static async Task<string> CallDynamicsWithCornetData(IConfiguration configuration, CornetTransaction model)
        {
            Log.Debug("CallDynamicsWithCornetData started");
            HttpClient httpClient = null;
            try
            {
                var cornetData = model.ToCornetDynamicsModel();
                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.NullValueHandling = NullValueHandling.Ignore;
                var cornetJson = JsonConvert.SerializeObject(cornetData, settings);
                cornetJson = cornetJson.Replace("odatatype", "@odata.type");

                // Get results into the tuple
                var endpointAction = "vsd_CreateCORNETNotifications";
                Log.Debug("Calling Dynamics endpoint {EndpointAction}", endpointAction);
                var tuple = await GetDynamicsHttpClientNew(configuration, cornetJson, endpointAction);
                Log.Debug("Response received from Dynamics");

                string tempResult = tuple.Item1.ToString();

                string tempJson = tuple.Item3.ToString();
                tempJson = tempJson.Replace("@odata.context", "oDataContext");

                DynamicsResponseModel deserializeJson = JsonConvert.DeserializeObject<DynamicsResponseModel>(tempJson);

                DynamicsResponse dynamicsResponse = new DynamicsResponse();

                dynamicsResponse.IsSuccess = deserializeJson.IsSuccess;
                dynamicsResponse.Result = deserializeJson.Result;

                if (dynamicsResponse.Result == null)
                {
                    dynamicsResponse.odatacontext = tempJson;
                }
                else
                {
                    dynamicsResponse.odatacontext = dynamicsResponse.Result;
                }

                Log.Debug("Returning results from Dynamics");
                return dynamicsResponse.odatacontext;

            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled exception in CallDynamicsWithCornetData");
                throw;
            }
            finally
            {
                if (httpClient != null)
                    httpClient.Dispose();
            }
        }

        static async Task<Tuple<int, HttpResponseMessage, string>> GetDynamicsHttpClientNew(IConfiguration configuration, String model, String endPointName)
        {
            Log.Debug("GetDynamicsHttpClientNew called for endpoint {EndpointName}", endPointName);
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>();
            var Configuration = builder.Build();
            Log.Debug("Configuration built");

            // Bind Dynamics configuration
            var dynamicsOptions =
                Configuration.GetSection("Dynamics").Get<DynamicsTokenProviderOptions>()
                ?? new DynamicsTokenProviderOptions();

            string dynamicsOdataUri = dynamicsOptions.GetDynamicsApiEndpointUrl();
            if (string.IsNullOrEmpty(dynamicsOdataUri))
            {
                Log.Error("Configuration setting for DynamicsApiEndpointUrl is blank");
                throw new Exception("Configuration setting for DynamicsApiEndpointUrl is blank.");
            }

            Log.Debug("Dynamics OData URI and options resolved");

            try
            {
                // Setup dependency injection for HTTP client factory
                var services = new ServiceCollection();
                services.AddHttpClient();
                var serviceProvider = services.BuildServiceProvider();
                var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                // Create token provider using the helper
                var tokenProvider = DynamicsAuthHelper.CreateTokenProvider(Configuration, httpClientFactory);

                // Acquire token
                string token = await tokenProvider.AcquireToken();
                Log.Debug("Dynamics OAuth token acquired");

                // Call Dynamics
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                string url = dynamicsOdataUri + endPointName;
                Log.Debug("Posting to Dynamics URL {DynamicsUrl}", url);

                HttpRequestMessage _httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                _httpRequest.Content = new StringContent(model, Encoding.UTF8, "application/json");
                Log.Debug("HTTP request prepared");

                var _httpResponse = await client.SendAsync(_httpRequest);
                HttpStatusCode _statusCode = _httpResponse.StatusCode;

                var _responseString = _httpResponse.ToString();
                Log.Debug("HTTP response received from Dynamics with status {StatusCode}", _statusCode);

                if (!_httpResponse.IsSuccessStatusCode)
                {
                    Log.Warning("Dynamics returned non-success HTTP {StatusCode} for endpoint {EndpointName}", (int)_statusCode, endPointName);
                }

                var _responseContent = await _httpResponse.Content.ReadAsStringAsync();

                Log.Debug("Dynamics request payload: {Model}", model);
                Log.Debug("Dynamics response string: {ResponseString}", _responseString);
                Log.Debug("Dynamics response content: {ResponseContent}", _responseContent);

                Log.Debug("GetDynamicsHttpClientNew completed successfully");
                return new Tuple<int, HttpResponseMessage, string>((int)_statusCode, _httpResponse, _responseContent);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled exception in GetDynamicsHttpClientNew for endpoint {EndpointName}", endPointName);
                return new Tuple<int, HttpResponseMessage, string>(100, null, "Error: " + e.Message);
            }
        }

        [HttpPost("InsertCornetTransaction")]
        public IActionResult InsertCornetTransaction(CornetTransaction cornetTransaction)
        {
            try
            {
                Log.Information("InsertCornetTransaction called");
                CornetTransactionRegistrationReply casregreply = new CornetTransactionRegistrationReply();
                CornetTransactionRegistration.getInstance().Add(cornetTransaction);
                casregreply.ResponseMessage = "Success";

                Log.Information("InsertCornetTransaction succeeded");
                return Ok(casregreply);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled exception in InsertCornetTransaction");
                return StatusCode(e.HResult);
            }

        }

        internal class DynamicsResponse
        {
            public string odatacontext { get; set; }
            public bool IsSuccess { get; set; }
            public bool IsCompletedSuccessfully { get; set; }
            public string Result { get; set; }
        }
    }
}
