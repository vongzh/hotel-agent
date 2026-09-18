using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Contracts;
using StayOta.Agent.Abstractions.Options;
using StayOta.Agent.Plugins.Refund.Ai;
using StayOta.Agent.Plugins.Refund.Mcp;
using StayOta.Agent.Plugins.Refund.Persistence;
using StayOta.Agent.Plugins.Refund.Production;
using StayOta.Agent.Plugins.Refund.Services;

namespace StayOta.Agent.Plugins.Refund;

public static class RefundPluginServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Refund vertical: EF (schema-isolated), 33 tools, rules, orchestrator, production clients, MCP.
    /// Requires <c>AddStayOtaAgent</c> first.
    /// </summary>
    public static IServiceCollection AddRefundPlugin(this IServiceCollection services, IConfiguration configuration)
    {
        var hosting = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>() ?? new HostingOptions();
        var isProductionLike = !hosting.DemoEnabled;

        var pg = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(pg))
        {
            if (isProductionLike)
                throw new InvalidOperationException("ConnectionStrings:Postgres is required when DemoEnabled=false");
            pg = "Host=127.0.0.1;Port=5432;Database=stayota_refund;Username=stayota;Password=stayota";
        }

        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(pg);
        });

        services.AddHttpClient("production", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<ProductionOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<IRefundDataStore, RefundDataStore>();
        services.AddScoped<IScenarioCatalog, ScenarioCatalog>();
        services.AddSingleton<IIntentService, IntentService>();
        services.AddSingleton<IPolicyRetrieval, PolicyRetrieval>();
        services.AddScoped<IRulesEngine, RulesEngine>();
        services.AddScoped<IToolGateway, ToolGateway>();
        services.AddScoped<IRefundAiToolCatalog, RefundAiToolCatalog>();

        services.AddScoped<MockProductionOrderClient>();
        services.AddScoped<HttpProductionOrderClient>();
        services.AddScoped<McpProductionOrderClient>();
        services.AddScoped<IExternalMcpToolSource, ExternalMcpToolSource>();
        services.AddScoped<IProductionOrderClient>(sp =>
        {
            var mode = sp.GetRequiredService<IOptions<ProductionOptions>>().Value.Mode;
            return mode.ToLowerInvariant() switch
            {
                "http" => sp.GetRequiredService<HttpProductionOrderClient>(),
                "mcp" => sp.GetRequiredService<McpProductionOrderClient>(),
                _ => sp.GetRequiredService<MockProductionOrderClient>()
            };
        });

        services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.AddScoped<IScenarioWorkflow, ScenarioWorkflow>();
        services.AddSingleton<IVerifier, Verifier>();
        services.AddScoped<IEvalRunner, EvalRunner>();

        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<RefundMcpTools>();

        return services;
    }
}
