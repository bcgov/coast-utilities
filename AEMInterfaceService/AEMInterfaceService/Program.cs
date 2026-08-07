using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace AEMInterfaceService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();


            //var config = new ConfigurationBuilder().AddEnvironmentVariables("").Build();
            //var url = config["ASPNETCORE_URLS"] ?? "http://*:8080";
            //var host = new WebHostBuilder()
            //    .UseKestrel()
            //    .UseContentRoot(Directory.GetCurrentDirectory())
            //    .UseIISIntegration()
            //    //.UseStartup()
            //    .UseUrls(url)
            //    .Build();
            //host.Run();


            //var config = new ConfigurationBuilder().AddEnvironmentVariables("").Build();
            //var url = config["ASPNETCORE_URLS"] ?? "http://*:8080";
            //var host = new WebHostBuilder()
            //    .UseKestrel()
            //    .UseContentRoot(Directory.GetCurrentDirectory())
            //    .UseIISIntegration()
            //    //.UseStartup()
            //    .UseUrls(url)
            //    .Build();
            //host.Run();

            //var host = new WebHostBuilder()
            //.UseKestrel(options =>
            //{
            //    // options.ThreadCount = 4;
            //    options.NoDelay = true;
            //    options.UseConnectionLogging();
            //})
            //.UseKestrel()
            //.UseUrls("http://*:8080")
            //.UseContentRoot(Directory.GetCurrentDirectory())
            //.UseStartup<Startup>()
            //.Build();

            // The following section should be used to demo sockets
            //var addresses = application.GetAddresses();
            //addresses.Clear();
            //addresses.Add("http://unix:/tmp/kestrel-test.sock");

            //host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls("http://*:8080");
                    webBuilder.UseStartup<Startup>();
                });

    }

}
