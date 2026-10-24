using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chess.Llm;

public static class LlmServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddLlmGateway(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ILlmGateway, FakeLlmGateway>();
        return builder;
    }
}
