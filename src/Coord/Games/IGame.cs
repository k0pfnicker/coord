namespace Coord.Games;

public interface IGame
{
    string Id { get; }
    string Name { get; }
}

public interface IGameRegistry
{
    void Register(IGame game);
    bool TryFind(string id, out IGame? game);
}

public sealed class GameRegistry : IGameRegistry
{
    private readonly Dictionary<string, IGame> games = new(StringComparer.OrdinalIgnoreCase);
    public void Register(IGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(game.Id)) throw new ArgumentException("Game id is required.", nameof(game));
        if (!games.TryAdd(game.Id, game)) throw new InvalidOperationException($"Game '{game.Id}' is already registered.");
    }
    public bool TryFind(string id, out IGame? game) => games.TryGetValue(id, out game);
    public IReadOnlyList<IGame> List() => games.Values.OrderBy(g => g.Id).ToArray();
}

public sealed record BuiltInGame(string Id, string Name) : IGame;
