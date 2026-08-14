using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AEMInterfaceService.Pages.Models;
using AEMInterfaceService.Pages.Models.Extensions;
using Gov.Cscp.VictimServices.Public.JsonObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oracle.ManagedDataAccess.Client;
using Shared.Database;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace AEMInterfaceService.Pages.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class AEMTransactionController : Controller
    {
        private readonly IConfiguration _configuration;

        public AEMTransactionController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // POST: api/<controller>
        [HttpPost]
        public async Task<AEMTransactionRegistrationReply> RegisterAEMTransaction(
            [FromBody] AEMTransaction aemTransaction
        )
        {
            Console.WriteLine(DateTime.UtcNow + " In RegisterAEMTransaction");

            // Set code to read secrets
            var builder = new ConfigurationBuilder().AddEnvironmentVariables().AddUserSecrets<Program>(); // must also define a project guid for secrets in the .cspro – add tag <UserSecretsId> containing a guid
            var Configuration = builder.Build();

            //TODO update these to be stored secrets
            string uri = Configuration["ORACLE_CONNECTION_URL"];
            Console.WriteLine(DateTime.UtcNow + " Got Oracle Connection URL");

            string username = Configuration["ORACLE_URL_USERID"];
            string password = Configuration["ORACLE_URL_PASSWORD"];
            Console.WriteLine(DateTime.UtcNow + " Got Login/Password information");

            HttpClient _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes($"{username}:{password}"))
            );

            Console.WriteLine(DateTime.UtcNow + " About to start Step 1");

            //step 1 - get render_url
            string endpointUrl2 =
                uri
                + "/adobeords/web/adobegetrenderurl?document_format="
                + aemTransaction.document_format
                + "&policy=victim";
            Console.WriteLine(DateTime.UtcNow + " Got Endpoint: " + endpointUrl2);
            HttpRequestMessage _httpRequest2 = new HttpRequestMessage(HttpMethod.Get, endpointUrl2);
            Console.WriteLine(DateTime.UtcNow + " Made httpRequest: " + _httpRequest2.RequestUri);
            var _httpResponse2 = await _client.SendAsync(_httpRequest2);
            Console.WriteLine(DateTime.UtcNow + " Got response: " + _httpResponse2.StatusCode);
            AdobeGetRenderURLResponse _responseContent2 =
                await _httpResponse2.Content.ReadAsAsync<AdobeGetRenderURLResponse>();
            Console.WriteLine(DateTime.UtcNow + " Step 1 Complete");

            //step 3 - get content_guid
            // Convert xml from base 64 to xml string
            var tempAEMXML = System.Xml.Linq.XElement.Load(
                new System.IO.MemoryStream(Convert.FromBase64String(aemTransaction.aem_xml_data))
            );
            Console.WriteLine(DateTime.UtcNow + " Working with this XML: " + tempAEMXML);
            //string endpointUrl = uri + "/adobeords/web/adobesavexml?documentContentText=" + tempAEMXML.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            string endpointUrl = uri + "/adobeords/web/adobesavexml";
            Console.WriteLine(DateTime.UtcNow + " Got the endpoint: " + endpointUrl);
            HttpRequestMessage _httpRequest = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
            Console.WriteLine(DateTime.UtcNow + " Made the _httpRequest");

            var jsonRequest = string.Format(
                    "$!$\"documentContentText\":\"{0}\"$&$",
                    tempAEMXML.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)
                )
                .Replace("$!$", "{")
                .Replace("$&$", "}");
            _httpRequest.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            Console.WriteLine(DateTime.UtcNow + " Created the httpRequest.Content: " + jsonRequest);

            var _httpResponse = await _client.SendAsync(_httpRequest);
            Console.WriteLine(DateTime.UtcNow + " Sent for _httpResponse: " + _httpResponse);

            AdobeSaveXMLResponse _responseContent = await _httpResponse.Content.ReadAsAsync<AdobeSaveXMLResponse>();
            Console.WriteLine(DateTime.UtcNow + " Got ResponseContent:" + _responseContent);
            Console.WriteLine(DateTime.UtcNow + " Step 2 Complete: " + _responseContent.pKey);

            //step 3 - call render_url with updated params?? Need clarification on what to do after step 1 and 2
            string endpointUrl3 = _responseContent2.render_url;
            Console.WriteLine(DateTime.UtcNow + " Got Endpoint: " + endpointUrl3);
            Console.WriteLine(DateTime.UtcNow + " About to update <<APP>: " + aemTransaction.AEMApp);
            endpointUrl3 = endpointUrl3.Replace("<<APP>>", aemTransaction.AEMApp);
            Console.WriteLine(DateTime.UtcNow + " About to update <<FORM>: " + aemTransaction.AEMForm);
            endpointUrl3 = endpointUrl3.Replace("<<FORM>>", aemTransaction.AEMForm);
            Console.WriteLine(DateTime.UtcNow + " About to update <<TICKET>: " + _responseContent.pKey);
            endpointUrl3 = endpointUrl3.Replace("<<TICKET>>", _responseContent.pKey);
            endpointUrl3 = endpointUrl3.Replace(Configuration["RESPONSE_URL"], Configuration["GATEWAY_URL"]);
            endpointUrl3 = endpointUrl3.Replace("https://prod.", "https://"); // This is a little workaround to get rid of the PROD prefix in PROD environment, if it exists
            Console.WriteLine(DateTime.UtcNow + " Fixed Endpoint: " + endpointUrl3);
            Console.WriteLine(DateTime.UtcNow + " Step 3 Complete");

            AEMTransactionRegistrationReply aemregreply = new AEMTransactionRegistrationReply();
            AEMTransactionRegistration.getInstance().Add(aemTransaction);

            Console.WriteLine(DateTime.UtcNow + " Received data from Dynamics");

            try
            {
                // Now we're just sending the URL back to Dynamics
                aemregreply.ResponseCode = "200";
                aemregreply.ResponseMessage = endpointUrl3;
                Console.WriteLine(DateTime.UtcNow + " Response Success");
            }
            catch (Exception e)
            {
                aemregreply.ResponseCode = "999";
                aemregreply.ResponseMessage = e.Message;
            }

            Console.WriteLine(DateTime.UtcNow + " Exit RegisterAEMTransaction");
            return aemregreply;
        }

        /// <summary>
        /// Tegisters the transaction with AEM and immediately returns the rendered PDF binary
        /// </summary>
        /// <param name="aemTransaction"></param>
        /// <returns></returns>
        [HttpPost("RegisterAndRetrievePdf")]
        public async Task<IActionResult> RegisterAndRetrievePdf([FromBody] AEMTransaction aemTransaction)
        {
            Console.WriteLine(
                DateTime.UtcNow
                    + " In RegisterAndRetrievePdf - aem_app="
                    + aemTransaction.AEMApp
                    + " aem_form="
                    + aemTransaction.AEMForm
                    + " document_format="
                    + aemTransaction.document_format
            );

            var builder = new ConfigurationBuilder().AddEnvironmentVariables().AddUserSecrets<Program>();
            var configuration = builder.Build();

            string uri = configuration["ORACLE_CONNECTION_URL"];
            string username = configuration["ORACLE_URL_USERID"];
            string password = configuration["ORACLE_URL_PASSWORD"];

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes($"{username}:{password}"))
            );

            // Step 1 – get render URL template from AEM
            string renderUrlEndpoint =
                uri
                + "/adobeords/web/adobegetrenderurl?document_format="
                + aemTransaction.document_format
                + "&policy=victim";

            Console.WriteLine(DateTime.UtcNow + " RegisterAndRetrievePdf: Step 1 - " + renderUrlEndpoint);

            var renderUrlResponse = await client.GetAsync(renderUrlEndpoint);

            Console.WriteLine(
                DateTime.UtcNow + " RegisterAndRetrievePdf: Step 1 - response " + renderUrlResponse.StatusCode
            );

            if (!renderUrlResponse.IsSuccessStatusCode)
                return StatusCode((int)renderUrlResponse.StatusCode, "Failed to retrieve render URL from AEM.");

            AdobeGetRenderURLResponse renderUrlContent =
                await renderUrlResponse.Content.ReadAsAsync<AdobeGetRenderURLResponse>();

            Console.WriteLine(
                DateTime.UtcNow + " RegisterAndRetrievePdf: Step 1 Complete - render_url=" + renderUrlContent.render_url
            );

            // Step 2 – save XML to AEM to get the pKey (ticket)
            var tempAEMXML = System.Xml.Linq.XElement.Load(
                new System.IO.MemoryStream(Convert.FromBase64String(aemTransaction.aem_xml_data))
            );

            string saveXmlEndpoint = uri + "/adobeords/web/adobesavexml";

            Console.WriteLine(DateTime.UtcNow + " RegisterAndRetrievePdf: Step 2 - " + saveXmlEndpoint);

            var jsonRequest = string.Format(
                    "$!$\"documentContentText\":\"{0}\"$&$",
                    tempAEMXML.ToString(System.Xml.Linq.SaveOptions.DisableFormatting)
                )
                .Replace("$!$", "{")
                .Replace("$&$", "}");
            var saveXmlRequest = new HttpRequestMessage(HttpMethod.Post, saveXmlEndpoint)
            {
                Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json"),
            };
            var saveXmlResponse = await client.SendAsync(saveXmlRequest);

            Console.WriteLine(
                DateTime.UtcNow + " RegisterAndRetrievePdf: Step 2 - response " + saveXmlResponse.StatusCode
            );

            if (!saveXmlResponse.IsSuccessStatusCode)
                return StatusCode((int)saveXmlResponse.StatusCode, "Failed to save XML to AEM.");

            AdobeSaveXMLResponse saveXmlContent = await saveXmlResponse.Content.ReadAsAsync<AdobeSaveXMLResponse>();

            Console.WriteLine(DateTime.UtcNow + " RegisterAndRetrievePdf: Step 2 Complete");

            // Step 3 – resolve placeholders to build the PDF URL
            string pdfUrl = renderUrlContent.render_url;
            pdfUrl = pdfUrl.Replace("<<APP>>", aemTransaction.AEMApp);
            pdfUrl = pdfUrl.Replace("<<FORM>>", aemTransaction.AEMForm);
            pdfUrl = pdfUrl.Replace("<<TICKET>>", saveXmlContent.pKey);
            pdfUrl = pdfUrl.Replace(configuration["RESPONSE_URL"], configuration["GATEWAY_URL"]);
            pdfUrl = pdfUrl.Replace("https://prod.", "https://");

            Console.WriteLine(DateTime.UtcNow + " RegisterAndRetrievePdf: Step 3 Complete - resolved PDF URL: " + pdfUrl);

            AEMTransactionRegistration.getInstance().Add(aemTransaction);

            // Step 4 – fetch the PDF and return it directly to the caller
            Console.WriteLine(DateTime.UtcNow + " RegisterAndRetrievePdf: Step 4 - fetching PDF");

            var pdfResponse = await client.GetAsync(pdfUrl, HttpCompletionOption.ResponseHeadersRead);

            Console.WriteLine(DateTime.UtcNow + " RegisterAndRetrievePdf: Step 4 - response " + pdfResponse.StatusCode);

            if (!pdfResponse.IsSuccessStatusCode)
                return StatusCode((int)pdfResponse.StatusCode, "AEM returned an error when fetching the PDF.");

            var pdfStream = await pdfResponse.Content.ReadAsStreamAsync();

            string contentType = pdfResponse.Content.Headers.ContentType?.ToString() ?? "application/pdf";

            Console.WriteLine(DateTime.UtcNow + " Exit RegisterAndRetrievePdf");

            return File(pdfStream, contentType);
        }

        private static async Task<string> CallDynamicsWithAEMData(IConfiguration configuration, AEMTransaction model)
        {
            Console.WriteLine(DateTime.UtcNow + " In CallDynamicsWithAEMData");
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
                Console.WriteLine(DateTime.UtcNow + " Set endpoint " + endpointAction);
                var tuple = await GetDynamicsHttpClientNew(configuration, aemJson, endpointAction);
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

        static async Task<Tuple<int, HttpResponseMessage, string>> GetDynamicsHttpClientNew(
            IConfiguration configuration,
            String model,
            String endPointName
        )
        {
            Console.WriteLine(DateTime.UtcNow + " In GetDynamicsHttpClientNew");
            var builder = new ConfigurationBuilder().AddEnvironmentVariables().AddUserSecrets<Program>();
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

        internal class DynamicsResponse
        {
            public string odatacontext { get; set; }
            public bool IsSuccess { get; set; }
            public bool IsCompletedSuccessfully { get; set; }
            public string Result { get; set; }
        }
    }
}
