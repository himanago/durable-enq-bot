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
            var client = LineMessagingClient.Create(Environment.GetEnvironmentVariable("ChannelAccessToken"));

            builder.Services
                .AddSingleton<ILineMessagingClient>(_ => client)
                .AddSingleton<EnqBotApp>(_ => new EnqBotApp(client, Environment.GetEnvironmentVariable("ChannelSecret")));
        }
    }
}