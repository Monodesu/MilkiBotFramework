﻿using Microsoft.Extensions.DependencyInjection;
using MilkiBotFramework.Platforms.GoCqHttp.Connecting;
using MilkiBotFramework.Platforms.GoCqHttp.ContactsManaging;
using MilkiBotFramework.Platforms.GoCqHttp.Dispatching;
using MilkiBotFramework.Platforms.GoCqHttp.Messaging;
using MilkiBotFramework.Plugining.CommandLine;

namespace MilkiBotFramework.Platforms.GoCqHttp
{
    public static class BotBuilderExtensions
    {
        public static BotBuilder UseGoCqHttp(this BotBuilder builder, string uri)
        {
            return builder
                .ConfigureServices(k =>
                {
                    k.AddScoped(typeof(GoCqMessageContext));
                })
                .UseConnector<GoCqWsClient>(uri)
                .UseDispatcher<GoCqDispatcher>()
                .UseCommandLineAnalyzer<CommandLineAnalyzer>(new GoCqParameterConverter())
                .UseRichMessageConverter<GoCqMessageConverter>()
                .UseContractsManager<GoCqContactsManager>()
                .UseMessageApi<GoCqApi>();
        }
    }
}
