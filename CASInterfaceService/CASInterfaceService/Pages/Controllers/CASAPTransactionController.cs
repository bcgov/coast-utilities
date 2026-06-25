using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using CASInterfaceService.Pages.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace CASInterfaceService.Pages.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class CASAPTransactionController : Controller
    {
        private string URL = "";
        private string TokenURL = "";
        private string clientID = "";
        private string secret = "";

        [HttpPost]
        public async Task<JObject> RegisterCASAPTransaction(CASAPTransaction casAPTransaction)
        {

            // Get secret information
            Log.Debug("Loading configuration and secrets for CASAPTransactionController");
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>(); // must also define a project guid for secrets in the .cspro – add tag <UserSecretsId> containing a guid
            var Configuration = builder.Build();
            URL = Configuration["CAS_API_URI"] + "cfs/apinvoice/"; // CAS AP URL
            TokenURL = Configuration["CAS_API_URI"] + "oauth/token"; // CAS AP Token URL
            clientID = Configuration["CAS_CLIENT_ID"];
            secret = Configuration["CAS_CLIENT_SECRET"];

            Log.Information("RegisterCASAPTransaction called for invoice {InvoiceNumber}", casAPTransaction.invoiceNumber);
            CASAPTransactionRegistrationReply casregreply = new CASAPTransactionRegistrationReply();
            CASAPTransactionRegistration.getInstance().Add(casAPTransaction);



            // Now we must call CAS with this data
            string outputMessage;

            try
            {
                // Start by getting token
                Log.Debug("Requesting OAuth token from {TokenUrl}", TokenURL);

                HttpClientHandler handler = new HttpClientHandler();
                HttpClient client = new HttpClient(handler);

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(string.Format("{0}:{1}", clientID, secret))));

                var request = new HttpRequestMessage(HttpMethod.Post, TokenURL);

                var formData = new List<KeyValuePair<string, string>>();
                formData.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));

                Log.Debug("Adding client credentials to token request");
                request.Content = new FormUrlEncodedContent(formData);
                var response = await client.SendAsync(request);

                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                Log.Debug("Token endpoint responded with HTTP {StatusCode}", response.StatusCode);
                response.EnsureSuccessStatusCode();

                // Put token alone in responseToken
                string responseBody = await response.Content.ReadAsStringAsync();
                var jo = JObject.Parse(responseBody);
                string responseToken = jo["access_token"].ToString();

                Log.Information("OAuth token acquired, submitting invoice {InvoiceNumber} to CAS", casAPTransaction.invoiceNumber);

                // Token received, now send package using token
                using (var packageClient = new HttpClient())
                {
                    packageClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", responseToken);
                    var jsonString = JsonConvert.SerializeObject(casAPTransaction);
                    HttpContent postContent = new StringContent(jsonString);
                    Log.Debug("Posting invoice payload to CAS: {InvoiceJson}", jsonString);
                    HttpResponseMessage packageResult = await packageClient.PostAsync(URL, postContent);

                    Log.Debug("CAS AP endpoint responded with HTTP {StatusCode}", packageResult.StatusCode);
                    outputMessage = Convert.ToString(packageResult.Content.ReadAsStringAsync().Result);
                    Log.Debug("CAS response body: {ResponseBody}", outputMessage);

                    if (!packageResult.IsSuccessStatusCode)
                    {
                        Log.Warning("CAS rejected invoice {InvoiceNumber}: HTTP {StatusCode} - {ResponseBody}", casAPTransaction.invoiceNumber, (int)packageResult.StatusCode, outputMessage);
                        dynamic errorObject = new JObject();
                        errorObject.invoice_number = casAPTransaction.invoiceNumber;
                        errorObject.CAS_Returned_Messages = "CAS Error " + (int)packageResult.StatusCode + ": " + outputMessage;
                        return errorObject;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled exception in RegisterCASAPTransaction for invoice {InvoiceNumber}", casAPTransaction.invoiceNumber);
                dynamic errorObject = new JObject();
                errorObject.invoice_number = null;
                errorObject.CAS_Returned_Messages = "Generic Error: " + e.Message;
                return errorObject;
            }

            var xjo = JObject.Parse(outputMessage);
            Log.Information("Successfully submitted invoice {InvoiceNumber} to CAS", casAPTransaction.invoiceNumber);
            return xjo;
        }

        [HttpPost("InsertCASAPTransaction")]
        public IActionResult InsertCASAPTransaction(CASAPTransaction casAPTransaction)
        {
            try
            {
                Log.Information("InsertCASAPTransaction called for invoice {InvoiceNumber}", casAPTransaction.invoiceNumber);
                CASAPTransactionRegistrationReply casregreply = new CASAPTransactionRegistrationReply();
                CASAPTransactionRegistration.getInstance().Add(casAPTransaction);
                casregreply.RegistrationStatus = "Success";

                Log.Information("InsertCASAPTransaction succeeded for invoice {InvoiceNumber}", casAPTransaction.invoiceNumber);
                return Ok(casregreply);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled exception in InsertCASAPTransaction for invoice {InvoiceNumber}", casAPTransaction.invoiceNumber);
                return StatusCode(e.HResult);
            }

        }
    }
}
