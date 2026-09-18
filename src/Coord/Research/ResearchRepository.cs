using System.Text.Json;
using Coord.Storage;

namespace Coord.Research;

public sealed record IdentityDossier(
    string Category, string Name, IReadOnlyList<string> Aliases,
    DateTimeOffset SelectedAt, string Provider = "manual");

public sealed record PlayerStats(
    string PlayerId, int GamesPlayed = 0, int Wins = 0,
    int Questions = 0, int Guesses = 0, int CorrectGuesses = 0)
{
    public PlayerStats RecordGame(bool won) =>
        this with { GamesPlayed = GamesPlayed + 1, Wins = Wins + (won ? 1 : 0) };
    public PlayerStats RecordQuestion() => this with { Questions = Questions + 1 };
    public PlayerStats RecordGuess(bool correct) =>
        this with { Guesses = Guesses + 1, CorrectGuesses = CorrectGuesses + (correct ? 1 : 0) };
}

public interface IResearchRepository
{
    Task SaveIdentityAsync(IdentityDossier dossier, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IdentityDossier>> LoadIdentitiesAsync(CancellationToken cancellationToken = default);
    Task<PlayerStats> UpdateStatsAsync(string playerId, Func<PlayerStats, PlayerStats> update,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlayerStats>> LoadLeaderboardAsync(CancellationToken cancellationToken = default);
}

public sealed class JsonResearchRepository(IStorage storage) : IResearchRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string IdentitiesKey = "research/identities.json";
    private const string StatsKey = "research/player-stats.json";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task SaveIdentityAsync(IdentityDossier dossier, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadAsync<List<IdentityDossier>>(IdentitiesKey, cancellationToken) ?? [];
            values.Add(dossier);
            await WriteAsync(IdentitiesKey, values, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<IdentityDossier>> LoadIdentitiesAsync(CancellationToken cancellationToken = default) =>
        await ReadAsync<List<IdentityDossier>>(IdentitiesKey, cancellationToken) ?? [];

    public async Task<PlayerStats> UpdateStatsAsync(string playerId, Func<PlayerStats, PlayerStats> update,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player id is required.", nameof(playerId));
        ArgumentNullException.ThrowIfNull(update);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadAsync<List<PlayerStats>>(StatsKey, cancellationToken) ?? [];
            var current = values.FirstOrDefault(x => x.PlayerId == playerId) ?? new PlayerStats(playerId);
            var next = update(current) with { PlayerId = playerId };
            values.RemoveAll(x => x.PlayerId == playerId);
            values.Add(next);
            await WriteAsync(StatsKey, values, cancellationToken);
            return next;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<PlayerStats>> LoadLeaderboardAsync(CancellationToken cancellationToken = default) =>
        (await ReadAsync<List<PlayerStats>>(StatsKey, cancellationToken) ?? [])
            .OrderByDescending(x => x.Wins).ThenByDescending(x => x.CorrectGuesses)
            .ThenBy(x => x.PlayerId, StringComparer.Ordinal).ToArray();

    private async Task<T?> ReadAsync<T>(string key, CancellationToken cancellationToken)
    {
        var bytes = await storage.GetAsync(key, cancellationToken);
        return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, Json);
    }
    private Task WriteAsync<T>(string key, T value, CancellationToken cancellationToken) =>
        storage.PutAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json), cancellationToken);
}
