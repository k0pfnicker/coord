namespace Coord.Ai;

public interface IAiProvider
{
    string Name { get; }
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}
