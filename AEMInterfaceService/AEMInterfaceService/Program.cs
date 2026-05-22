using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AEMInterfaceService
{
    public class Program
    {
        private const string URL = "https://<server>:<port>ords/cas/cfs/apinvoice/";
        private const string TokenURL = "https://<server>:<port>/ords/casords/oauth/token";

        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseUrls("http://*:8080")
                .UseStartup<Startup>();

        public void CallCAS()
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(URL);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenURL);
            //client.PostAsJsonAsync<HttpResponseMessage>(client.BaseAddress,new HttpResponseMessage tmpResponse());
            //var result = await client.GetAsync(URL);
            


            //OAuthResponse requestToken = OAuth.AcquireRequestToken("http://www.www.com", "Post");


            WebRequest request = WebRequest.Create("http://www.temp.com/?param1=x&param2=y");
            request.Method = "GET";
            WebResponse response = request.GetResponse();



            //var client = new RestClient("https://service.endpoint.com/api/oauth2/token");
            //var request = new RestRequest(Method.POST);
            //request.AddHeader("cache-control", "no-cache");
            //request.AddHeader("content-type", "application/x-www-form-urlencoded");
            //request.AddParameter("application/x-www-form-urlencoded", "grant_type=client_credentials&client_id=abc&client_secret=123", ParameterType.RequestBody);
            //IRestResponse response = client.Execute(request);





            //// List data response.
            //HttpResponseMessage response = client.GetAsync(urlParameters).Result;  // Blocking call! Program will wait here until a response is received or a timeout occurs.
            //if (response.IsSuccessStatusCode)
            //{
            //    // Parse the response body.
            //    var dataObjects = response.Content.ReadAsAsync<IEnumerable<DataObject>>().Result;  //Make sure to add a reference to System.Net.Http.Formatting.dll
            //    foreach (var d in dataObjects)
            //    {
            //        Console.WriteLine("{0}", d.Name);
            //    }
            //}
            //else
            //{
            //    Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            //}

            //Make any other calls using HttpClient here.

            //Dispose once all HttpClient calls are complete. This is not necessary if the containing object will be disposed of; for example in this case the HttpClient instance will be disposed automatically when the application terminates so the following call is superfluous.
            client.Dispose();
        }

    }

}
