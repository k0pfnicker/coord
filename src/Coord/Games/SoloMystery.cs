using System.Text.Json;
using Coord.Ai;

namespace Coord.Games;

public enum SoloMysteryPhase { Configuring, Active, Won, Failed, Aborted }

public sealed record MysteryScene(string Scene, string Clue, string Status, string? Reason);
public sealed record SoloMysteryPublicState(
    SoloMysteryPhase Phase, string? PlayerId, string? Scene, string? Clue,
    IReadOnlyList<string> History, string? Result);
public sealed record SoloMysteryAction(bool Accepted, string Reason, SoloMysteryPublicState State);

public interface ISoloMysteryProvider
{
    Task<string> NextSceneAsync(string prompt, CancellationToken cancellationToken = default);
}

public sealed class AiMysteryProvider(IAiProvider provider) : ISoloMysteryProvider
{
    public Task<string> NextSceneAsync(string prompt, CancellationToken cancellationToken = default) =>
        provider.CompleteAsync(prompt, cancellationToken);
}

public sealed class DeterministicMysteryProvider(IReadOnlyList<string>? scenes = null) : ISoloMysteryProvider
{
    private readonly IReadOnlyList<string> script = scenes ?? [
        """{"scene":"A locked archive waits beneath the station.","clue":"A brass key bears a crescent mark.","status":"continue"}""",
        """{"scene":"The archive opens onto a room of stopped clocks.","clue":"Only the clock reflected in the window is moving.","status":"continue"}""",
        """{"scene":"The final mechanism clicks open.","clue":"The crescent key fits the clockwork lock.","status":"won","reason":"The mystery is solved."}"""
    ];
    private int index;
    public Task<string> NextSceneAsync(string prompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(script[Math.Min(Interlocked.Increment(ref index) - 1, script.Count - 1)]);
}

public static class MysteryPromptBuilder
{
    public static string Build(string premise, string solution, string action, IReadOnlyList<string> history) =>
        "You are the host of a short text mystery. Return ONLY one JSON object with keys " +
        "scene, clue, status (continue|won|failed), and optional reason.\n" +
        $"Premise: {premise}\nHidden solution: {solution}\nPlayer action or question: {action}\n" +
        $"Previous turns: {string.Join(" | ", history)}\n" +
        "Never reveal, quote, or spell out the hidden solution. Do not include markdown, extra keys, " +
        "instructions, player data, or claims outside the story. Mark won only when the action solves it; " +
        "mark failed only when the player has irreversibly failed.";
}

public static class MysteryOutputSanitizer
{
    public static MysteryScene Parse(string output, string hiddenSolution)
    {
        var clean = output.Trim();
        if (clean.StartsWith("```", StringComparison.Ordinal))
        {
            clean = clean[(clean.IndexOf('\n') + 1)..];
            var fence = clean.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) clean = clean[..fence];
        }
        try
        {
            using var doc = JsonDocument.Parse(clean);
            var root = doc.RootElement;
            var scene = Required(root, "scene");
            var clue = Required(root, "clue");
            var status = Required(root, "status").ToLowerInvariant();
            if (status is not ("continue" or "won" or "failed"))
                throw new FormatException("Invalid mystery status.");
            if (ContainsSecret(scene, hiddenSolution) || ContainsSecret(clue, hiddenSolution))
                throw new FormatException("Provider output contained the hidden solution.");
            var reason = root.TryGetProperty("reason", out var reasonValue) ? reasonValue.GetString() : null;
            if (reason is not null && ContainsSecret(reason, hiddenSolution))
                throw new FormatException("Provider output contained the hidden solution.");
            return new MysteryScene(Bound(scene, 1200)!, Bound(clue, 600)!, status, Bound(reason, 400));
        }
        catch (JsonException ex) { throw new AiProviderException("Mystery provider returned invalid JSON.", ex); }
    }

    private static string Required(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!.Trim() :
        throw new FormatException($"Mystery response requires '{name}'.");
    private static bool ContainsSecret(string text, string secret) =>
        !string.IsNullOrWhiteSpace(secret) && text.Contains(secret, StringComparison.OrdinalIgnoreCase);
    private static string? Bound(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}

public sealed class SoloMysteryGame(ISoloMysteryProvider provider, string premise = "A vanished researcher left a trail in an abandoned station.", string solution = "the crescent key opens the clockwork lock")
{
    private readonly object sync = new();
    private readonly List<string> history = [];
    private SoloMysteryPhase phase = SoloMysteryPhase.Configuring;
    private string? playerId;
    private string? scene;
    private string? clue;
    private string? result;
    public SoloMysteryPublicState State { get { lock (sync) return Snapshot(); } }

    public bool AddPlayer(string id)
    {
        lock (sync)
        {
            if (phase != SoloMysteryPhase.Configuring || playerId is not null) return false;
            playerId = id;
            return true;
        }
    }
    public SoloMysteryAction Abort(string reason) { lock (sync) { phase = SoloMysteryPhase.Aborted; result = reason; return Accept(reason); } }
    public async Task<SoloMysteryAction> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (phase != SoloMysteryPhase.Configuring) return Reject("Mystery is not configuring.");
            if (playerId is null) return Reject("Exactly one player is required.");
            phase = SoloMysteryPhase.Active;
        }
        return await AdvanceAsync("begin", cancellationToken);
    }
    public Task<SoloMysteryAction> SubmitAsync(string id, string action, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (phase != SoloMysteryPhase.Active) return Task.FromResult(Reject("Mystery is not active."));
            if (id != playerId) return Task.FromResult(Reject("This mystery admits exactly one player."));
            if (string.IsNullOrWhiteSpace(action) || action.Length > 500) return Task.FromResult(Reject("Action is required and must be under 500 characters."));
        }
        return AdvanceAsync(action.Trim(), cancellationToken);
    }
    private async Task<SoloMysteryAction> AdvanceAsync(string action, CancellationToken cancellationToken)
    {
        string prompt;
        lock (sync) prompt = MysteryPromptBuilder.Build(premise, solution, action, history.ToArray());
        MysteryScene next;
        try { next = MysteryOutputSanitizer.Parse(await provider.NextSceneAsync(prompt, cancellationToken), solution); }
        catch (OperationCanceledException)
        { lock (sync) { phase = SoloMysteryPhase.Aborted; result = "Mystery provider timed out or was cancelled."; return Reject(result); } }
        catch (Exception ex) when (ex is AiProviderException or FormatException or HttpRequestException)
        { lock (sync) { phase = SoloMysteryPhase.Aborted; result = "Mystery provider failed safely."; return Reject(result); } }
        lock (sync)
        {
            scene = next.Scene; clue = next.Clue; history.Add(action);
            phase = next.Status switch { "won" => SoloMysteryPhase.Won, "failed" => SoloMysteryPhase.Failed, _ => SoloMysteryPhase.Active };
            result = next.Reason;
            return Accept(next.Reason ?? "Scene advanced.");
        }
    }
    private SoloMysteryAction Accept(string reason) => new(true, reason, Snapshot());
    private SoloMysteryAction Reject(string reason) => new(false, reason, Snapshot());
    private SoloMysteryPublicState Snapshot() => new(phase, playerId, scene, clue, history.ToArray(), result);
}
