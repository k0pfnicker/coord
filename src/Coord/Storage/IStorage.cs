namespace Coord.Storage;

public interface IStorage
{
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task PutAsync(string key, byte[] value, CancellationToken cancellationToken = default);
}
