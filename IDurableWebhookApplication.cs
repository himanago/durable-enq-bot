using LineDC.Messaging.Webhooks;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace Sample.LineBot
{
    public interface IDurableWebhookApplication : IWebhookApplication
    {
        ILogger Logger { get; set; }
        IDurableOrchestrationClient DurableClient { get; set; } 
    }
}