using Microsoft.Extensions.DependencyInjection;

namespace ApplicationAssistant.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationAssistantAi(this IServiceCollection services)
    {
        services.AddSingleton<ILLMService, GeminiLLMService>();
        services.AddSingleton<ICvParser, CvParser>();
        services.AddSingleton<ICvGenerator, CvGenerator>();
        return services;
    }
}
