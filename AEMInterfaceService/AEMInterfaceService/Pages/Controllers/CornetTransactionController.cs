using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AEMInterfaceService.Pages.Models;
using Gov.Cscp.VictimServices.Public.JsonObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AEMInterfaceService.Pages.Models.Extensions;
using Microsoft.AspNetCore.Http;
using Shared.Database;
using Microsoft.Extensions.DependencyInjection;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace AEMInterfaceService.Pages.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CornetTransactionController : Controller
    {
        private readonly IConfiguration _configuration;
        //private readonly IHttpContextAccessor _httpContextAccessor;

        public CornetTransactionController(IConfiguration configuration)//, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            //_httpContextAccessor = httpContextAccessor;
        }


        // POST: api/<controller>
        [HttpPost]
        public CornetTransactionRegistrationReply RegisterCornetTransaction(CornetTransaction cornetTransaction)
        {
            Console.WriteLine(DateTime.UtcNow + " In RegisterCornetTransaction");
            CornetTransactionRegistrationReply cornetregreply = new CornetTransactionRegistrationReply();
            CornetTransactionRegistration.getInstance().Add(cornetTransaction);
            Console.WriteLine(DateTime.UtcNow + " Received data from Cornet");

            var t = Task.Run(() => CallDynamicsWithCornetData(_configuration, cornetTransaction));
            t.Wait();
            Console.WriteLine(DateTime.UtcNow + " Sent data to Dynamics");

            if (t.Result.Contains("Cornet Notification "))
            {
                cornetregreply.ResponseCode = "200";
                cornetregreply.ResponseMessage = "Success";
                Console.WriteLine(DateTime.UtcNow + " Response Success");
            }
            else
            {
                cornetregreply.ResponseMessage = "Failure";
                cornetregreply.ResponseCode = t.Result;
                Console.WriteLine(DateTime.UtcNow + " Response Fail");
                //}
            }
            

            Console.WriteLine(DateTime.UtcNow + " Exit RegisterCornetTransaction");
            return cornetregreply;

        }

        private static async Task<string> CallDynamicsWithCornetData(IConfiguration configuration, CornetTransaction model)
        {
            Console.WriteLine(DateTime.UtcNow + " In CallDynamicsWithCornetData");
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
                Console.WriteLine(DateTime.UtcNow + " Set endpoint " + endpointAction);
                var tuple = await GetDynamicsHttpClientNew(configuration, cornetJson, endpointAction);
                Console.WriteLine(DateTime.UtcNow + " Got result from Dynamics");

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

                Console.WriteLine(DateTime.UtcNow + " Return results from Dynamics");
                return dynamicsResponse.odatacontext;

            }
            finally
            {
                if (httpClient != null)
                    httpClient.Dispose();
            }
        }

        static async Task<Tuple<int, HttpResponseMessage, string>> GetDynamicsHttpClientNew(IConfiguration configuration, String model, String endPointName)
        {
            Console.WriteLine(DateTime.UtcNow + " In GetDynamicsHttpClientNew");
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>();
            var Configuration = builder.Build();
            Console.WriteLine(DateTime.UtcNow + " Build Configuration");

            // Bind Dynamics configuration
            var dynamicsOptions =
                Configuration.GetSection("Dynamics").Get<DynamicsTokenProviderOptions>()
                ?? new DynamicsTokenProviderOptions();

            string dynamicsOdataUri = dynamicsOptions.GetDynamicsApiEndpointUrl();
            if (string.IsNullOrEmpty(dynamicsOdataUri))
            {
                throw new Exception("Configuration setting for DynamicsApiEndpointUrl is blank.");
            }

            Console.WriteLine(DateTime.UtcNow + " Variables have been set");

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
                Console.WriteLine(DateTime.UtcNow + " Got a token");

                // Call Dynamics
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                string url = dynamicsOdataUri + endPointName;
                Console.WriteLine(DateTime.UtcNow + " Set full URL to speak to Dynamics: " + url);

                HttpRequestMessage _httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                _httpRequest.Content = new StringContent(model, Encoding.UTF8, "application/json");
                Console.WriteLine(DateTime.UtcNow + " Got HTTP Request ready");

                var _httpResponse = await client.SendAsync(_httpRequest);
                HttpStatusCode _statusCode = _httpResponse.StatusCode;

                var _responseString = _httpResponse.ToString();
                Console.WriteLine(DateTime.UtcNow + " Got HTTP Response");
                var _responseContent = await _httpResponse.Content.ReadAsStringAsync();

                Console.Out.WriteLine(DateTime.UtcNow + " model: " + model);
                Console.Out.WriteLine(DateTime.UtcNow + " responseString: " + _responseString);
                Console.Out.WriteLine(DateTime.UtcNow + " responseContent: " + _responseContent);

                Console.WriteLine(DateTime.UtcNow + " Exit GetDynamicsHttpClientNew");
                return new Tuple<int, HttpResponseMessage, string>((int)_statusCode, _httpResponse, _responseContent);
            }
            catch (Exception e)
            {
                Console.WriteLine(DateTime.UtcNow + " Error in GetDynamicsHttpClientNew: " + e.Message);
                return new Tuple<int, HttpResponseMessage, string>(100, null, "Error: " + e.Message);
            }
        }

        [HttpPost("InsertCornetTransaction")]
        public IActionResult InsertCornetTransaction(CornetTransaction cornetTransaction)
        {
            try
            {
                Console.WriteLine(DateTime.UtcNow + " In InsertCornetTransaction");
                CornetTransactionRegistrationReply casregreply = new CornetTransactionRegistrationReply();
                CornetTransactionRegistration.getInstance().Add(cornetTransaction);
                casregreply.ResponseMessage = "Success";

                return Ok(casregreply);
            }
            catch (Exception e)
            {
                Console.WriteLine(DateTime.UtcNow + " Error in InsertCornetTransaction. " + e.ToString());
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
