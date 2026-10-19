using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CornetInterfaceService.Pages.Models;
using CornetInterfaceService.Pages.Models.Extensions;
using Gov.Cscp.VictimServices.Public.JsonObjects;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using Shared.Database;

namespace CornetInterfaceService.Pages.Services
{
    /// <summary>
    /// Result of posting a CORNET transaction to Dynamics.
    /// </summary>
    public sealed record CornetNotificationResult(bool IsSuccess, string ResponseCode);

    public interface IDynamicsCornetNotificationClient
    {
        Task<CornetNotificationResult> SendCornetNotificationAsync(CornetTransaction model);
    }

    /// <summary>
    /// Owns the CORNET-to-Dynamics integration: payload mapping, OAuth token acquisition,
    /// the outbound OData HTTP call, and response parsing. Kept out of the controller so the
    /// controller is limited to request mapping, invoking this client, and shaping the reply.
    /// </summary>
    public class DynamicsCornetNotificationClient : IDynamicsCornetNotificationClient
    {
        private const string EndpointAction = "vsd_CreateCORNETNotifications";

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ITokenProvider _tokenProvider;

        public DynamicsCornetNotificationClient(
            HttpClient httpClient,
            IConfiguration configuration,
            ITokenProvider tokenProvider
        )
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _tokenProvider = tokenProvider;
        }

        public async Task<CornetNotificationResult> SendCornetNotificationAsync(CornetTransaction model)
        {
            try
            {
                var cornetData = model.ToCornetDynamicsModel();

                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                };
                var cornetJson = JsonConvert.SerializeObject(cornetData, settings);
                cornetJson = cornetJson.Replace("odatatype", "@odata.type");

                var result = await PostToDynamicsAsync(cornetJson, EndpointAction);

                string tempJson = result.Content;

                if (!IsValidJson(tempJson))
                {
                    Log.Error(
                        "Dynamics response for Cornet transaction {EventMessageId} was not valid JSON: {ResponseContent}",
                        model.event_message_id,
                        tempJson
                    );
                    return new CornetNotificationResult(false, tempJson);
                }

                tempJson = tempJson.Replace("@odata.context", "oDataContext");

                DynamicsResponseModel deserializeJson = JsonConvert.DeserializeObject<DynamicsResponseModel>(tempJson);

                DynamicsResponse dynamicsResponse = new DynamicsResponse
                {
                    IsSuccess = deserializeJson.IsSuccess,
                    Result = deserializeJson.Result,
                };

                dynamicsResponse.odatacontext = dynamicsResponse.Result ?? tempJson;

                var isSuccess = dynamicsResponse.odatacontext.Contains("Cornet Notification ");
                return new CornetNotificationResult(isSuccess, dynamicsResponse.odatacontext);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error calling Dynamics for Cornet transaction {EventMessageId}", model.event_message_id);
                throw;
            }
        }

        private async Task<(int StatusCode, string Content)> PostToDynamicsAsync(string model, string endPointName)
        {
            // Bind Dynamics configuration
            var dynamicsOptions =
                _configuration.GetSection("Dynamics").Get<DynamicsTokenProviderOptions>()
                ?? new DynamicsTokenProviderOptions();

            string dynamicsOdataUri = dynamicsOptions.GetDynamicsApiEndpointUrl();
            if (string.IsNullOrEmpty(dynamicsOdataUri))
            {
                throw new Exception("Configuration setting for DynamicsApiEndpointUrl is blank.");
            }

            try
            {
                // Acquire token from the shared, cached token provider (registered once in Program.cs)
                string token = await _tokenProvider.AcquireToken();
                Log.Debug("Acquired Dynamics token");

                // _httpClient is a typed client (services.AddHttpClient<IDynamicsCornetNotificationClient, DynamicsCornetNotificationClient>())
                // so its handler is pooled/rotated by IHttpClientFactory instead of being created per call.
                if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
                {
                    _httpClient.DefaultRequestHeaders.Remove("Authorization");
                }
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                string url = dynamicsOdataUri + endPointName;
                Log.Debug("Calling Dynamics endpoint {Url}", url);

                using HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
                httpRequest.Content = new StringContent(model, Encoding.UTF8, "application/json");

                using var httpResponse = await _httpClient.SendAsync(httpRequest);
                
                var responseContent = await httpResponse.Content.ReadAsStringAsync();
                Log.Debug("Dynamics response {StatusCode}: {ResponseContent}", (int)httpResponse.StatusCode, responseContent);

                return ((int)httpResponse.StatusCode, responseContent);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error calling Dynamics endpoint {EndpointName}", endPointName);
                return (100, "Error: " + e.Message);
            }
        }

        private static bool IsValidJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
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
