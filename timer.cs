using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FunctionApp1
{
    public class timer
    {
        private readonly ILogger _logger;

        public timer(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<timer>();
        }

        [Function("timer")]
        public void Run([TimerTrigger("*/5 * * * * *")] MyInfo myTimer)
        {
            try
            {
                throw new Exception();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);

                throw new Exception(ex.StackTrace);
            }

            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            //_logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
        }
    }

    public class MyInfo
    {
        public MyScheduleStatus ScheduleStatus { get; set; }

        public bool IsPastDue { get; set; }
    }

    public class MyScheduleStatus
    {
        public DateTime Last { get; set; }

        public DateTime Next { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
