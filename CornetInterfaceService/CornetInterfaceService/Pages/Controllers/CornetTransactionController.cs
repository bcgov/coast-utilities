using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CASInterfaceService.Pages.Models;
using Gov.Cscp.VictimServices.Public.JsonObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using CASInterfaceService.Pages.Models.Extensions;
using Serilog;
using Shared.Database;
using Microsoft.Extensions.DependencyInjection;

namespace CASInterfaceService.Pages.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CornetTransactionController : Controller
    {
        private readonly IConfiguration _configuration;

        public CornetTransactionController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost]
        public CornetTransactionRegistrationReply RegisterCornetTransaction(CornetTransaction cornetTransaction)
        {
            CornetTransactionRegistrationReply cornetregreply = new CornetTransactionRegistrationReply();
            CornetTransactionRegistration.getInstance().Add(cornetTransaction);
            Log.Information("Received Cornet transaction {EventMessageId}", cornetTransaction.event_message_id);
            try
            {
                var t = Task.Run(() => CallDynamicsWithCornetData(_configuration, cornetTransaction));
                t.Wait();

                if (t.Result.Contains("Cornet Notification "))
                {
                    cornetregreply.ResponseCode = "200";
                    cornetregreply.ResponseMessage = "Success";
                    Log.Information("Cornet transaction {EventMessageId} processed successfully", cornetTransaction.event_message_id);
                }
                else
                {
                    cornetregreply.ResponseMessage = "Failure";
                    cornetregreply.ResponseCode = t.Result;
                    Log.Warning("Cornet transaction {EventMessageId} failed with code {ResponseCode}", cornetTransaction.event_message_id, t.Result);
                }

                return cornetregreply;

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error Registering Cornet Transaction: {EventMessageId}", cornetTransaction.event_message_id);
                cornetregreply.ResponseMessage = "Failure";
                cornetregreply.ResponseCode = "200"; // counterintuitive, but cornet might expect this and not retry if it sees a different code
                return cornetregreply;
            }
        }

        private static async Task<string> CallDynamicsWithCornetData(IConfiguration configuration, CornetTransaction model)
        {
            try
            {
                var cornetData = model.ToCornetDynamicsModel();
                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.NullValueHandling = NullValueHandling.Ignore;
                var cornetJson = JsonConvert.SerializeObject(cornetData, settings);
                cornetJson = cornetJson.Replace("odatatype", "@odata.type");

                // Get results into the tuple
                var endpointAction = "vsd_CreateCORNETNotifications";
                var tuple = await GetDynamicsHttpClientNew(configuration, cornetJson, endpointAction);

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

                return dynamicsResponse.odatacontext;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error calling Dynamics for Cornet transaction {EventMessageId}", model.event_message_id);
                throw;
            }
        }

        static async Task<Tuple<int, HttpResponseMessage, string>> GetDynamicsHttpClientNew(IConfiguration configuration, String model, String endPointName)
        {
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>();
            var Configuration = builder.Build();

            // Bind Dynamics configuration
            var dynamicsOptions =
                Configuration.GetSection("Dynamics").Get<DynamicsTokenProviderOptions>()
                ?? new DynamicsTokenProviderOptions();

            string dynamicsOdataUri = dynamicsOptions.GetDynamicsApiEndpointUrl();
            if (string.IsNullOrEmpty(dynamicsOdataUri))
            {
                throw new Exception("Configuration setting for DynamicsApiEndpointUrl is blank.");
            }

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
                Log.Debug("Acquired Dynamics token");

                // Call Dynamics
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                string url = dynamicsOdataUri + endPointName;
                Log.Debug("Calling Dynamics endpoint {Url}", url);

                HttpRequestMessage _httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                _httpRequest.Content = new StringContent(model, Encoding.UTF8, "application/json");

                var _httpResponse = await client.SendAsync(_httpRequest);
                HttpStatusCode _statusCode = _httpResponse.StatusCode;

                var _responseContent = await _httpResponse.Content.ReadAsStringAsync();
                Log.Debug("Dynamics response {StatusCode}: {ResponseContent}", (int)_statusCode, _responseContent);

                return new Tuple<int, HttpResponseMessage, string>((int)_statusCode, _httpResponse, _responseContent);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error calling Dynamics endpoint {EndpointName}", endPointName);
                return new Tuple<int, HttpResponseMessage, string>(100, null, "Error: " + e.Message);
            }
        }

        [HttpPost("InsertCornetTransaction")]
        public IActionResult InsertCornetTransaction(CornetTransaction cornetTransaction)
        {
            try
            {
                CornetTransactionRegistrationReply casregreply = new CornetTransactionRegistrationReply();
                CornetTransactionRegistration.getInstance().Add(cornetTransaction);
                casregreply.ResponseMessage = "Success";

                return Ok(casregreply);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in InsertCornetTransaction");
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
