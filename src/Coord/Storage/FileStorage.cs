namespace Coord.Storage;

public sealed class FileStorage : IStorage
{
    private readonly string root;
    private readonly SemaphoreSlim gate = new(1, 1);

    public FileStorage(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("A data directory is required.", nameof(dataDirectory));
        root = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(root);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    public async Task PutAsync(string key, byte[] value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var path = Resolve(key);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await gate.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllBytesAsync(temporary, value, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            gate.Release();
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(key)) throw new ArgumentException("Storage keys must be relative and safe.", nameof(key));
        return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
