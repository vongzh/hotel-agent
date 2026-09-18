using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using StayOta.Agent.Abstractions.Ai;
using StayOta.Agent.Abstractions.Options;

namespace StayOta.Agent.Plugins.Refund.Mcp;

/// <summary>
/// Loads tools from an external production MCP endpoint when Production:Mode=Mcp.
/// </summary>
public sealed class ExternalMcpToolSource(
    IOptions<ProductionOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<ExternalMcpToolSource> logger) : IExternalMcpToolSource
{
    public async Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        if (!string.Equals(opts.Mode, "Mcp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(opts.McpEndpoint))
            return [];

        try
        {
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(opts.McpEndpoint),
                TransportMode = HttpTransportMode.AutoDetect,
                AdditionalHeaders = string.IsNullOrWhiteSpace(opts.ApiKey)
                    ? null
                    : new Dictionary<string, string> { ["Authorization"] = $"Bearer {opts.ApiKey}" }
            }, loggerFactory);

            await using var client = await McpClient.CreateAsync(
                transport,
                loggerFactory: loggerFactory,
                cancellationToken: ct);

            var tools = await client.ListToolsAsync(cancellationToken: ct);
            logger.LogInformation("Loaded {Count} tools from production MCP {Endpoint}", tools.Count, opts.McpEndpoint);
            return tools.Cast<AITool>().ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load external MCP tools from {Endpoint}", opts.McpEndpoint);
            return [];
        }
    }
}
