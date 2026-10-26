using Chess.Api.Persistence;
using Microsoft.Extensions.Options;

namespace Chess.Api.Requests;

public static class RequestExtensions
{
    /// <summary>
    /// Registers paid game requests (03 §15): the options with start-up validation, the menu, the queue and the intake. The
    /// queue is <see cref="IGameRequestQueue"/> for the broadcast runner. The recovery of a request left playing runs at start-up.
    /// </summary>
    public static IHostApplicationBuilder AddRequests(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<RequestOptions>()
            .Bind(builder.Configuration.GetSection("Arena:Requests"))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<RequestOptions>, RequestOptionsValidator>();

        builder.Services.AddSingleton<RequestMenu>();
        builder.Services.AddSingleton<GameRequestQueue>();
        builder.Services.AddSingleton<IGameRequestQueue>(services => services.GetRequiredService<GameRequestQueue>());
        builder.Services.AddSingleton<IChatIntake, ChatIntake>();
        builder.Services.AddHostedService<RequestRecovery>();
        return builder;
    }
}
