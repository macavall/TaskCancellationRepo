using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Azure;
using Azure.Core;
using System.Linq;
using System.Net.Http;
using Polly;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Azure.Identity;
using Polly.Extensions.Http;
using System.Net;
using Azure;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Azure.EventGrid;

internal class Program
{
    private static void Main(string[] args)
    {
        var host = new HostBuilder()
        .ConfigureFunctionsWorkerDefaults()
        .ConfigureOpenApi()
        .ConfigureLogging(logging => logging.AddFilter<ApplicationInsightsLoggerProvider>(null, LogLevel.Information))
        .ConfigureAppConfiguration(builder => Startup.ConfigureAppConfiguration(builder))

        .Build();

        host.Run();
    }
}

internal static class Startup
{
    internal static void ConfigureAppConfiguration(
        IConfigurationBuilder builder
        )
    {
        const string Sentinel = "Rbac:Sentinel";
        string[] Keys = { "Config:Rbac:*" };
        StartupShared.ConfigureAppConfiguration(builder, Sentinel, Keys);
    }

    internal static void Configure(
        IServiceCollection services
        )
    {
        StartupShared.ConfigureServices(services);

        TokenCredential credential = StartupShared.GetDefaultAzureCredential();

        services.AddAzureClients(builder =>
        {
            if (StartupShared.IsRunningLocally())
            {
                // Direct connection string when running locally
                // IgnoreException(SecurityException)
                builder.AddServiceBusClient(Environment.GetEnvironmentVariable("serviceBusConnectionString"));
            }
            else
            {
                // Managed identity in Azure
                builder.AddServiceBusClientWithNamespace(Environment.GetEnvironmentVariable("serviceBusNameSpace__fullyQualifiedNamespace")).WithCredential(StartupShared.GetDefaultAzureCredential());
            }

            var eventGridTopicEndpointUri = Environment.GetEnvironmentVariable(FunctionConstants.EventGridEndpointSetting);
            var eventGridTopicAccessKey = Environment.GetEnvironmentVariable(FunctionConstants.EventGridTopicSetting);
            builder.AddEventGridPublisherClient(new Uri(eventGridTopicEndpointUri!), new AzureKeyCredential(eventGridTopicAccessKey!));
        });

        StartupCosmos.ConfigureServices(services, credential);
        StartupServiceHelpers.ConfigureServices(services);
        services.AddControllers();

        services.AddScoped<IModuleConfiguration, ModuleConfiguration>();
        services.AddScoped<IServiceBusNotificationClient, ServiceBusNotificationClient>();
        services.AddScoped<IEventGridNotificationClient, EventGridNotificationClient>();
        services.AddScoped<IEventGridClient<AuditEventModel>, AuditEventGridClient>();
        services.AddScoped<IRbacService, RbacService>();
        services.AddScoped<IRbacConfigurationManager, RbacConfigurationManager>();
        services.AddScoped<IRbacUtilities, RbacUtilities>();
        services.AddScoped<IRbacRequestValidator, RbacRequestValidator>();
        services.AddScoped<IRoleManager, RoleManager>();
        services.AddScoped<IRbacEmailManager, RbacEmailManager>();
        services.AddScoped<IEmailNotificationProvider, EmailNotificationProvider>();
        services.AddScoped<IOrganizationHelpers, OrganizationHelpers>();
        services.AddScoped<IRoleAssignmentManager, RoleAssignmentManager>();
        services.AddScoped<IRoleAssignmentHelpers, RoleAssignmentHelpers>();
        services.AddScoped<IAuditHelpers, AuditHelpers>();
        services.AddScoped<IRetryHelper, RetryHelper>();
        services.AddScoped<ITimer, SharedClientLibrary.Utilities.Timer>();
        services.AddScoped<ITaskWrapper, TaskWrapper>();
    }

    public static class StartupShared
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddSingleton<IAsyncPolicy<HttpResponseMessage>>(GetRetryPolicy());
            AzureAppConfigurationExtensions.AddAzureAppConfiguration(services);
            AzureClientServiceCollectionExtensions.AddAzureClients(services, (Action<AzureClientFactoryBuilder>)delegate (AzureClientFactoryBuilder builder)
            {
                if (IsRunningLocally())
                {
                    ConfigurationClientBuilderExtensions.AddConfigurationClient<AzureClientFactoryBuilder>(builder, Environment.GetEnvironmentVariable("appConfigurationConnectionString"));
                }
                else
                {
                    builder.AddClient<ConfigurationClient, ConfigurationClientOptions>((Func<ConfigurationClientOptions, ConfigurationClient>)((ConfigurationClientOptions options) => new ConfigurationClient(new Uri(Environment.GetEnvironmentVariable("appConfigurationEndpoint")), GetDefaultAzureCredential())));
                }
            });
            services.AddScoped<IExecutionContext, ExecutionContext>();
        }

        public static void ConfigureAppConfiguration(IConfigurationBuilder builder, string sentinel, string[] keys)
        {
            string sentinel2 = sentinel;
            string[] keys2 = keys;
            AzureAppConfigurationExtensions.AddAzureAppConfiguration(builder, (Action<AzureAppConfigurationOptions>)delegate (AzureAppConfigurationOptions options)
            {
                //IL_000f: Unknown result type (might be due to invalid IL or missing references)
                //IL_0019: Expected O, but got Unknown
                TokenCredential credential;
                AzureAppConfigurationOptions val;
                if (IsRunningLocally())
                {
                    credential = (TokenCredential)new DefaultAzureCredential(false);
                    val = options.Connect(Environment.GetEnvironmentVariable("appConfigurationConnectionString"));
                }
                else
                {
                    credential = GetDefaultAzureCredential();
                    val = options.Connect(new Uri(Environment.GetEnvironmentVariable("appConfigurationEndpoint")), GetDefaultAzureCredential());
                }
                val.ConfigureKeyVault((Action<AzureAppConfigurationKeyVaultOptions>)delegate (AzureAppConfigurationKeyVaultOptions kv)
                {
                    kv.SetCredential(credential);
                }).ConfigureRefresh((Action<AzureAppConfigurationRefreshOptions>)delegate (AzureAppConfigurationRefreshOptions refreshOptions)
                {
                    refreshOptions.SetCacheExpiration(TimeSpan.FromMinutes(5.0)).Register("Config:Sentinel", true).Register(sentinel2, true);
                });
                string[] array = keys2;
                foreach (string text in array)
                {
                    val.Select(text, "\0");
                }
            }, false);
        }

        public static void RemoveApplicationInsightsFilter(IServiceCollection services)
        {
            services.Configure(delegate (LoggerFilterOptions options)
            {
                LoggerFilterRule loggerFilterRule = options.Rules.FirstOrDefault((LoggerFilterRule rule) => rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
                if (loggerFilterRule != null)
                {
                    options.Rules.Remove(loggerFilterRule);
                }
            });
        }

        public static TokenCredential GetDefaultAzureCredential()
        {
            if (!IsRunningLocally())
            {
                return (TokenCredential)new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = Environment.GetEnvironmentVariable("functionsClientId"),
                    ExcludeSharedTokenCacheCredential = true,
                    ExcludeEnvironmentCredential = true
                });
            }
            return (TokenCredential)new DefaultAzureCredential(false);
        }

        public static bool IsRunningLocally()
        {
            return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
        }

        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return (IAsyncPolicy<HttpResponseMessage>)(object)AsyncRetryTResultSyntax.WaitAndRetryAsync<HttpResponseMessage>(HttpPolicyExtensions.HandleTransientHttpError().OrResult((Func<HttpResponseMessage, bool>)((HttpResponseMessage msg) => msg.StatusCode == HttpStatusCode.TooManyRequests || msg.StatusCode == HttpStatusCode.Unauthorized)), 5, (Func<int, TimeSpan>)((int retryAttempt) => TimeSpan.FromSeconds(5.0)));
        }
    }

    public static class StartupCosmos
    {
        public static void ConfigureServices(IServiceCollection services, TokenCredential credential)
        {
            TokenCredential credential2 = credential;
            string endPoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") ?? "";
            services.AddSingleton((IServiceProvider s) => new CosmosClientBuilder(endPoint, credential2).Build());
            services.AddSingleton<ICosmosDBStorage, CosmosDBStorage>();
        }

        private interface ICosmosDBStorage
        {
        }

        private class CosmosDBStorage : ICosmosDBStorage
        {
        }
    }

    public static class StartupServiceHelpers
    {
        public static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IEnvironmentWrapper, EnvironmentWrapper>();
            string storageConnectionString = Environment.GetEnvironmentVariable("storageConnectionString");
            AzureClientServiceCollectionExtensions.AddAzureClients(services, (Action<AzureClientFactoryBuilder>)delegate (AzureClientFactoryBuilder builder)
            {
                QueueClientBuilderExtensions.AddQueueServiceClient<AzureClientFactoryBuilder>(builder, storageConnectionString);
            });
            services.AddScoped<IOrganizationHelpers, OrganizationHelpers>();
        }

        private interface IEnvironmentWrapper
        {
        }



        private class  EnvironmentWrapper : IEnvironmentWrapper
        {
        }
        private interface IOrganizationHelpers
        {
        }

        private class OrganizationHelpers : IOrganizationHelpers
        {
        }
    }

    public interface ExecutionContext : IExecutionContext
    {
    }

    internal interface IExecutionContext
    {
    }

    private class FunctionConstants
    {
        public static string EventGridEndpointSetting { get; internal set; }
        public static string EventGridTopicSetting { get; internal set; }
    }

    private interface IModuleConfiguration
    {
    }

    private class ModuleConfiguration : IModuleConfiguration
    {
    }

    private interface IServiceBusNotificationClient
    {
    }

    private class ServiceBusNotificationClient: IServiceBusNotificationClient
    {
    }

    private interface IEventGridNotificationClient
    {
    }

    private class EventGridNotificationClient : IEventGridNotificationClient
    {
    }

    private class AuditEventModel
    {
    }

    private class AuditEventGridClient : IEventGridClient<AuditEventModel>
    {
    }

    private interface IEventGridClient<T>
    {
    }

    private interface IRbacService
    {
    }

    private class RbacService : IRbacService
    {
    }

    private interface IRbacConfigurationManager
    {
    }

    private class RbacConfigurationManager : IRbacConfigurationManager
    {
    }

    private interface IRbacUtilities
    {
    }

    private class RbacUtilities : IRbacUtilities
    {
    }

    private interface IRbacRequestValidator
    {
    }

    private class RbacRequestValidator : IRbacRequestValidator
    {
    }

    private interface IRoleManager
    {
    }

    private class RoleManager : IRoleManager
    {
    }

    private interface IRbacEmailManager
    {
    }

    private class RbacEmailManager : IRbacEmailManager
    {
    }

    private interface IEmailNotificationProvider
    {
    }

    private class EmailNotificationProvider : IEmailNotificationProvider
    {
    }

    private interface IOrganizationHelpers
    {
    }

    private class OrganizationHelpers : IOrganizationHelpers
    {
    }

    private interface IRoleAssignmentManager
    {
    }

    private class RoleAssignmentManager : IRoleAssignmentManager
    {
    }

    private interface IRoleAssignmentHelpers
    {
    }

    private class RoleAssignmentHelpers : IRoleAssignmentHelpers
    {
    }

    private interface IAuditHelpers
    {
    }

    private class AuditHelpers : IAuditHelpers
    {
    }

    private interface IRetryHelper
    {
    }

    private class RetryHelper : IRetryHelper
    {
    }

    private interface ITimer
    {
    }

    private class SharedClientLibrary
    {
        internal class Utilities
        {
            internal class Timer : ITimer
            {
            }
        }
    }

    private interface ITaskWrapper
    {
    }

    private class TaskWrapper : ITaskWrapper
    {
    }
}

