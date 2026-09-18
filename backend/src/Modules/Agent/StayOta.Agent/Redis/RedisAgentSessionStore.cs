using System.Text.Json;
using StackExchange.Redis;
using StayOta.Agent.Abstractions.Ai;

namespace StayOta.Agent.Redis;

public sealed class RedisAgentSessionStore(IConnectionMultiplexer mux) : IAgentSessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    public async Task SaveAsync(string sessionId, AgentSessionSnapshot snapshot, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        var json = JsonSerializer.Serialize(snapshot);
        await db.StringSetAsync($"agent:session:{sessionId}", json, Ttl);
    }

    public async Task<AgentSessionSnapshot?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        var db = mux.GetDatabase();
        var value = await db.StringGetAsync($"agent:session:{sessionId}");
        if (value.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<AgentSessionSnapshot>((string)value!);
    }
}
