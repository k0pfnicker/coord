namespace Coord.Search;

public interface ISearchProvider
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}

public sealed record SearchResult(string Id, string Title, double Score);
