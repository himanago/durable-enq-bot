using System;
using LineDC.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(Sample.LineBot.Startup))]
namespace Sample.LineBot
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services
                .AddSingleton<ILineMessagingClient>(_ => LineMessagingClient.Create(Environment.GetEnvironmentVariable("ChannelAccessToken")))
                .AddTransient<EnqBotApp>(s => new EnqBotApp(s.GetService<ILineMessagingClient>(), Environment.GetEnvironmentVariable("ChannelSecret")));
        }
    }
}