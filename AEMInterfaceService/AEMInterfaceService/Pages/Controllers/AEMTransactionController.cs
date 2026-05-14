using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AEMInterfaceService.Pages.Models;
using Gov.Cscp.VictimServices.Public.JsonObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AEMInterfaceService.Pages.Models.Extensions;
using Microsoft.AspNetCore.Http;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using Shared.Database;
using Microsoft.Extensions.DependencyInjection;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace AEMInterfaceService.Pages.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AEMTransactionController : Controller
    {
        //private string URL = "";
        //private string TokenURL = "";
        //private string clientID = "";
        //private string secret = "";


        private readonly IConfiguration _configuration;
        //private readonly IHttpContextAccessor _httpContextAccessor;

        public AEMTransactionController(IConfiguration configuration)//, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            //_httpContextAccessor = httpContextAccessor;
        }


        // POST: api/<controller>
        [HttpPost]
        public async Task<AEMTransactionRegistrationReply> RegisterAEMTransaction([FromBody] AEMTransaction aemTransaction)
        {
            Console.WriteLine(DateTime.Now + " In RegisterAEMTransaction");

            // Set code to read secrets
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>(); // must also define a project guid for secrets in the .cspro – add tag <UserSecretsId> containing a guid
            var Configuration = builder.Build();

            //TODO update these to be stored secrets
            string uri = Configuration["ORACLE_CONNECTION_URL"];
            Console.WriteLine(DateTime.Now + " Got Oracle Connection URL");

            string username = Configuration["ORACLE_URL_USERID"];
            string password = Configuration["ORACLE_URL_PASSWORD"];
            Console.WriteLine(DateTime.Now + " Got Login/Password information");

            HttpClient _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(
                        "Basic", Convert.ToBase64String(
                            System.Text.ASCIIEncoding.ASCII.GetBytes(
                               $"{username}:{password}")));


            Console.WriteLine(DateTime.Now + " About to start Step 1");

            //step 1 - get render_url
            string endpointUrl2 = uri + "/adobeords/web/adobegetrenderurl?document_format=" + aemTransaction.document_format + "&policy=victim";
            Console.WriteLine(DateTime.Now + " Got Endpoint: " + endpointUrl2);
            HttpRequestMessage _httpRequest2 = new HttpRequestMessage(HttpMethod.Get, endpointUrl2);
            Console.WriteLine(DateTime.Now + " Made httpRequest: " + _httpRequest2.RequestUri);
            var _httpResponse2 = await _client.SendAsync(_httpRequest2);
            Console.WriteLine(DateTime.Now + " Got response: " + _httpResponse2.StatusCode);
            AdobeGetRenderURLResponse _responseContent2 = await _httpResponse2.Content.ReadAsAsync<AdobeGetRenderURLResponse>();
            Console.WriteLine(DateTime.Now + " Step 1 Complete");

            //step 3 - get content_guid
            // Convert xml from base 64 to xml string
            var tempAEMXML = System.Xml.Linq.XElement.Load(new System.IO.MemoryStream(Convert.FromBase64String(aemTransaction.aem_xml_data)));
            Console.WriteLine(DateTime.Now + " Working with this XML: " + tempAEMXML);
            //string endpointUrl = uri + "/adobeords/web/adobesavexml?documentContentText=" + tempAEMXML.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            string endpointUrl = uri + "/adobeords/web/adobesavexml";
            Console.WriteLine(DateTime.Now + " Got the endpoint: " + endpointUrl);
            HttpRequestMessage _httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
            Console.WriteLine(DateTime.Now + " Made the _httpRequest");

            var jsonRequest = string.Format("$!$\"documentContentText\":\"{0}\"$&$", tempAEMXML.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)).Replace("$!$", "{").Replace("$&$", "}");
            _httpRequest.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            Console.WriteLine(DateTime.Now + " Created the httpRequest.Content: " + jsonRequest);

            var _httpResponse = await _client.SendAsync(_httpRequest);
            Console.WriteLine(DateTime.Now + " Sent for _httpResponse: " + _httpResponse);

            AdobeSaveXMLResponse _responseContent = await _httpResponse.Content.ReadAsAsync<AdobeSaveXMLResponse>();
            Console.WriteLine(DateTime.Now + " Got ResponseContent:" + _responseContent);
            Console.WriteLine(DateTime.Now + " Step 2 Complete: " + _responseContent.pKey);

            //step 3 - call render_url with updated params?? Need clarification on what to do after step 1 and 2
            string endpointUrl3 = _responseContent2.render_url;
            Console.WriteLine(DateTime.Now + " Got Endpoint: " + endpointUrl3);
            Console.WriteLine(DateTime.Now + " About to update <<APP>: " + aemTransaction.AEMApp);
            endpointUrl3 = endpointUrl3.Replace("<<APP>>", aemTransaction.AEMApp);
            Console.WriteLine(DateTime.Now + " About to update <<FORM>: " + aemTransaction.AEMForm);
            endpointUrl3 = endpointUrl3.Replace("<<FORM>>", aemTransaction.AEMForm);
            Console.WriteLine(DateTime.Now + " About to update <<TICKET>: " + _responseContent.pKey);
            endpointUrl3 = endpointUrl3.Replace("<<TICKET>>", _responseContent.pKey);
            endpointUrl3 = endpointUrl3.Replace(Configuration["RESPONSE_URL"], Configuration["GATEWAY_URL"]);
            endpointUrl3 = endpointUrl3.Replace("https://prod.", "https://"); // This is a little workaround to get rid of the PROD prefix in PROD environment, if it exists
            Console.WriteLine(DateTime.Now + " Fixed Endpoint: " + endpointUrl3);
            Console.WriteLine(DateTime.Now + " Step 3 Complete");

            AEMTransactionRegistrationReply aemregreply = new AEMTransactionRegistrationReply();
            AEMTransactionRegistration.getInstance().Add(aemTransaction);

            Console.WriteLine(DateTime.Now + " Received data from Dynamics");

            try
            {
                // Now we're just sending the URL back to Dynamics
                aemregreply.ResponseCode = "200";
                aemregreply.ResponseMessage = endpointUrl3;
                Console.WriteLine(DateTime.Now + " Response Success");
            }
            catch (Exception e)
            {
                aemregreply.ResponseCode = "999";
                aemregreply.ResponseMessage = e.Message;
            }

            Console.WriteLine(DateTime.Now + " Exit RegisterAEMTransaction");
            return aemregreply;

         }
        //private static async Task<string> CallAEMWithDynamicsData(IConfiguration configuration, AEMTransaction model)
        //{
        //    Console.WriteLine(DateTime.Now + " In CallAEMWithDynamicsData");
        //    return "success";

        //}

        private static async Task<string> CallDynamicsWithAEMData(IConfiguration configuration, AEMTransaction model)
        {
            Console.WriteLine(DateTime.Now + " In CallDynamicsWithAEMData");
            HttpClient httpClient = null;
            try
            {
                var aemData = model.ToAEMDynamicsModel();
                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.NullValueHandling = NullValueHandling.Ignore;
                var aemJson = JsonConvert.SerializeObject(aemData, settings);
                aemJson = aemJson.Replace("odatatype", "@odata.type");

                // Get results into the tuple
                var endpointAction = "vsd_CreateCORNETNotifications";
                Console.WriteLine(DateTime.Now + " Set endpoint " + endpointAction);
                var tuple = await GetDynamicsHttpClientNew(configuration, aemJson, endpointAction);
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

            string dynamicsOdataUri = dynamicsOptions.GetDynamicsApiEndpointUrl();
            if (string.IsNullOrEmpty(dynamicsOdataUri))
            {
                throw new Exception("Configuration setting for DynamicsApiEndpointUrl is blank.");
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
