using System;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FunctionApp1
{
    public class http1
    {
        private readonly ILogger _logger;

        public http1(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<http1>();
        }

        [Function("http1")]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req, ExecutionContext context)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            context.Dispose();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            response.WriteString("Welcome to Azure Functions!");

            return response;
        }
    }
}
