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
