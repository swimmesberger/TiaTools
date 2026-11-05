namespace TiaFormat;

/// <summary>
/// Hands out one file path per object of a dump. Names repeat in an archive and may differ only in case, or become equal
/// when made safe for a file system. Not thread safe: reserve all paths before writing in parallel.
/// </summary>
public sealed class TiaDumpPaths {
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The path itself if free, else with the object id and, if that is taken too, a counter before the extension.</summary>
    public string Reserve(string path, ulong objectId) {
        if (_taken.Add(path)) {
            return path;
        }
        var stem = Path.ChangeExtension(path, null);
        var extension = Path.GetExtension(path);
        var candidate = $"{stem}.{objectId}{extension}";
        for (var counter = 2; !_taken.Add(candidate); counter++) {
            candidate = $"{stem}.{objectId}.{counter}{extension}";
        }
        return candidate;
    }
}
