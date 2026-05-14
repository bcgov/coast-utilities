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

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace CASInterfaceService.Pages.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CornetTransactionController : Controller
    {
        private string URL = "";
        private string TokenURL = "";
        private string clientID = "";
        private string secret = "";

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
            Console.WriteLine(DateTime.Now + " In RegisterCornetTransaction");
            CornetTransactionRegistrationReply cornetregreply = new CornetTransactionRegistrationReply();
            CornetTransactionRegistration.getInstance().Add(cornetTransaction);
            Console.WriteLine(DateTime.Now + " Received data from Cornet");

            var t = Task.Run(() => CallDynamicsWithCornetData(_configuration, cornetTransaction));
            t.Wait();
            Console.WriteLine(DateTime.Now + " Sent data to Dynamics");

            if (t.Result.Contains("Cornet Notification "))
            {
                cornetregreply.ResponseCode = "200";
                cornetregreply.ResponseMessage = "Success";
                Console.WriteLine(DateTime.Now + " Response Success");
            }
            else
            {
                //JObject tempJson = JObject.Parse(t.Result);
                //CornetDynamicsReply replyJson = new CornetDynamicsReply();

                //if (t.IsCompletedSuccessfully == true)
                //{
                //    cornetregreply.ResponseMessage = "Success";
                //    cornetregreply.ResponseCode = null;// t.Result;
                //    Console.WriteLine(DateTime.Now + " Response Success");
                //}
                //else
                //{
                cornetregreply.ResponseMessage = "Failure";
                cornetregreply.ResponseCode = t.Result;
                Console.WriteLine(DateTime.Now + " Response Fail");
                //}
            }

            // Responses as follows:
            // 200 - Status OK - Automatically Done
            // 400 - Bad Request (Malformed JSON) - Automatically Done
            // 500 - Internal Server Error (Something wrong on our end)
            // 201 - If anything is being created on our end based on the notification sent
            // This next line is just a sample of how to do it:
            //this.HttpContext.Response.StatusCode = 444;

            Console.WriteLine(DateTime.Now + " Exit RegisterCornetTransaction");
            return cornetregreply;

        }

        private static async Task<string> CallDynamicsWithCornetData(IConfiguration configuration, CornetTransaction model)
        {
            Console.WriteLine(DateTime.Now + " In CallDynamicsWithCornetData");
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
                Console.WriteLine(DateTime.Now + " Set endpoint " + endpointAction);
                var tuple = await GetDynamicsHttpClientNew(configuration, cornetJson, endpointAction);
                Console.WriteLine(DateTime.Now + " Got result from Dynamics");

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

                Console.WriteLine(DateTime.Now + " Return results from Dynamics");
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
            Console.WriteLine(DateTime.Now + " In GetDynamicsHttpClientNew");
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>();
            var Configuration = builder.Build();
            Console.WriteLine(DateTime.Now + " Build Configuration");

            // Bind Dynamics configuration
            var dynamicsOptions =
                Configuration.GetSection("Dynamics").Get<DynamicsTokenProviderOptions>()
                ?? new DynamicsTokenProviderOptions();

            string dynamicsOdataUri = dynamicsOptions.DynamicsApiEndpointUrl;
            if (string.IsNullOrEmpty(dynamicsOdataUri))
            {
                throw new Exception("Configuration setting Dynamics:DynamicsApiEndpointUrl is blank.");
            }

            Console.WriteLine(DateTime.Now + " Variables have been set");

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
                Console.WriteLine(DateTime.Now + " Got a token");

                // Call Dynamics
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                string url = dynamicsOdataUri + endPointName;
                Console.WriteLine(DateTime.Now + " Set full URL to speak to Dynamics: " + url);

                HttpRequestMessage _httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                _httpRequest.Content = new StringContent(model, Encoding.UTF8, "application/json");
                Console.WriteLine(DateTime.Now + " Got HTTP Request ready");

                var _httpResponse = await client.SendAsync(_httpRequest);
                HttpStatusCode _statusCode = _httpResponse.StatusCode;

                var _responseString = _httpResponse.ToString();
                Console.WriteLine(DateTime.Now + " Got HTTP Response");
                var _responseContent = await _httpResponse.Content.ReadAsStringAsync();

                Console.Out.WriteLine(DateTime.Now + " model: " + model);
                Console.Out.WriteLine(DateTime.Now + " responseString: " + _responseString);
                Console.Out.WriteLine(DateTime.Now + " responseContent: " + _responseContent);

                Console.WriteLine(DateTime.Now + " Exit GetDynamicsHttpClientNew");
                return new Tuple<int, HttpResponseMessage, string>((int)_statusCode, _httpResponse, _responseContent);
            }
            catch (Exception e)
            {
                Console.WriteLine(DateTime.Now + " Error in GetDynamicsHttpClientNew: " + e.Message);
                return new Tuple<int, HttpResponseMessage, string>(100, null, "Error: " + e.Message);
            }
        }

        [HttpPost("InsertCornetTransaction")]
        public IActionResult InsertCornetTransaction(CornetTransaction cornetTransaction)
        {
            try
            {
                Console.WriteLine(DateTime.Now + " In InsertCornetTransaction");
                CornetTransactionRegistrationReply casregreply = new CornetTransactionRegistrationReply();
                CornetTransactionRegistration.getInstance().Add(cornetTransaction);
                casregreply.ResponseMessage = "Success";

                return Ok(casregreply);
            }
            catch (Exception e)
            {
                Console.WriteLine(DateTime.Now + " Error in InsertCornetTransaction. " + e.ToString());
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
