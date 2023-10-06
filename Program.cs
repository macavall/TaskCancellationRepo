using FunctionApp1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

internal class Program
{
    public static IHost staticHost;

    private static void Main(string[] args)
    {
        var host = new HostBuilder()
        .ConfigureFunctionsWorkerDefaults()
        .ConfigureServices((s) =>
        {
            s.AddScoped<IMyService, MyService>();
        })
        .Build();

        _ = Task.Factory.StartNew(() =>
        {
            var myServ = new MyService();

            _ = myServ.DoSomething();
        });

        staticHost = host;

        host.Run();
    }

    public class MyService : IMyService
    {
        private readonly ILogger log;

        public MyService()
        {

        }

        public async Task DoSomething()
        {
            //var myServ = new MyService();

            //_ = myServ.DoSomething();

            await StartAsyncOperation();

            static async Task StartAsyncOperation()
            {
                Console.WriteLine(Process.GetCurrentProcess().Threads.Count);
                await Task.Delay(100);
                await PerformAsyncOperation();
            }

            static async Task PerformAsyncOperation()
            {
                await Task.Delay(100);

                _ = SpinForever();

                _ = StartAsyncOperation();

                //throw new Exception("Something went wrong in PerformAsyncOperation!");
            }

            static async Task SpinForever()
            {
                await Task.Delay(1);

                if (Process.GetCurrentProcess().Threads.Count > 75)
                {
                    await staticHost.StopAsync();
                }

                while (true)
                {
                    Thread.SpinWait(999999);
                    Thread.Sleep(10000);
                    //Thread.Sleep(1000);
                }
            }


            //await Task.Delay(1);
            //_ = Task.Factory.StartNew(() =>
            //{
            //    _ = Task.Factory.StartNew(() =>
            //    {
            //        Thread.Sleep(1000);
            //        _ = DoSomething();
            //    });
            //});
        }
    }



    public interface IMyService
    {
        public Task DoSomething();
    }
}