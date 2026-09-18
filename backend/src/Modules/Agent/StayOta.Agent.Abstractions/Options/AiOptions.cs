namespace StayOta.Agent.Abstractions.Options;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Deterministic | OpenAI | Ollama</summary>
    public string Provider { get; set; } = "Deterministic";

    public OpenAiOptions OpenAI { get; set; } = new();
    public OllamaOptions Ollama { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    /// <summary>Optional OpenAI-compatible base URL (Azure/gateway).</summary>
    public string? Endpoint { get; set; }
}

public sealed class OllamaOptions
{
    public string Endpoint { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "llama3.2";
}
