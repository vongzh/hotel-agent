using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Ai;
using StayOta.Agent.Redis;

namespace StayOta.Agent;

public static class StayOtaAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared Agent runtime: options, Redis stores, chat client, conversation host.
    /// Call <c>AddRefundPlugin</c> (or another vertical plugin) afterwards.
    /// </summary>
    public static IServiceCollection AddStayOtaAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var hosting = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>() ?? new HostingOptions();
        var isProductionLike = !hosting.DemoEnabled;

        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            if (isProductionLike)
                throw new InvalidOperationException("ConnectionStrings:Redis is required when DemoEnabled=false");
            redis = "127.0.0.1:6379";
        }

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.Configure<ProductionOptions>(configuration.GetSection(ProductionOptions.SectionName));
        services.Configure<HostingOptions>(configuration.GetSection(HostingOptions.SectionName));
        services.Configure<AgentStorageOptions>(configuration.GetSection(AgentStorageOptions.SectionName));

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
        services.AddSingleton<IConfirmationStore, RedisConfirmationStore>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<ISessionStore, RedisSessionStore>();
        services.AddSingleton<IAgentSessionStore, RedisAgentSessionStore>();

        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<IChatClient>(sp =>
        {
            var hostOpts = sp.GetRequiredService<IOptions<HostingOptions>>().Value;
            try
            {
                return sp.GetRequiredService<IChatClientFactory>().Create();
            }
            catch (Exception ex)
            {
                if (!hostOpts.AllowDeterministicFallback)
                    throw;

                var logger = sp.GetService<ILoggerFactory>()?.CreateLogger("ChatClientRegistration");
                logger?.LogWarning(ex, "Falling back to DeterministicRefundChatClient");
                return new DeterministicRefundChatClient();
            }
        });

        services.AddScoped<IRefundAgentHost, RefundAgentHost>();
        services.AddScoped<IAgentConversationService, AgentConversationService>();

        return services;
    }
}
