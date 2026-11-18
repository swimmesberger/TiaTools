namespace TiaViewer;

/// <summary>
/// The files and folders the viewer opened, most recent first, kept in a text file with one full path per line.
/// What no longer exists is left out when the list is read, and dropped when it is next written.
/// Each change reads the file again first, so two viewers open at once do not undo each other's entries.
/// </summary>
public sealed class RecentFiles {
    /// <summary>How many it keeps.</summary>
    public const int Capacity = 10;

    // Windows and macOS compare file names without regard to case by default; Linux does not.
    private static readonly StringComparer PathComparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly string _file;
    private readonly Func<string, bool> _exists;
    private List<string> _paths;

    /// <param name="file">The text file that holds the list. Its folder is created when the list is first written.</param>
    /// <param name="exists">Whether a path still exists; by default, a file or folder on disk.</param>
    public RecentFiles(string file, Func<string, bool>? exists = null) {
        _file = file;
        _exists = exists ?? (path => File.Exists(path) || Directory.Exists(path));
        _paths = Read();
    }

    /// <summary>Where the list is kept: the platform's per-user application data, such as <c>~/Library/Application Support</c> on a Mac.</summary>
    public static string DefaultFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIA Viewer", "recent.txt");

    /// <summary>Raised after the list changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The paths that still exist, most recent first.</summary>
    public IReadOnlyList<string> Paths => _paths.Where(_exists).ToList();

    /// <summary>Puts a path first, removing it from further down, and keeps at most <see cref="Capacity"/>.</summary>
    public void Add(string path) {
        var full = Path.GetFullPath(path);
        if (full.Contains('\n') || full.Contains('\r')) {
            return;
        }
        Change(paths => {
            paths.RemoveAll(other => PathComparer.Equals(other, full));
            paths.Insert(0, full);
        });
    }

    /// <summary>Removes a path, for example one that could not be opened.</summary>
    public void Remove(string path) {
        var full = Path.GetFullPath(path);
        Change(paths => paths.RemoveAll(other => PathComparer.Equals(other, full)));
    }

    /// <summary>Forgets every path.</summary>
    public void Clear() => Change(paths => paths.Clear());

    /// <summary>
    /// What a menu shows for each path: its name, and where two have the same name, the name of the folder it is in as well.
    /// </summary>
    public static IReadOnlyList<string> Labels(IReadOnlyList<string> paths) {
        var names = paths.Select(NameOf).ToList();
        return names.Select((name, index) => names.Count(other => PathComparer.Equals(other, name)) > 1
            ? $"{name} — {NameOf(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(paths[index])) ?? "")}"
            : name).ToList();
    }

    private static string NameOf(string path) {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(trimmed);
        return name.Length == 0 ? trimmed : name;
    }

    private void Change(Action<List<string>> change) {
        var paths = Read();
        change(paths);
        _paths = paths.Where(_exists).Take(Capacity).ToList();
        Write(_paths);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<string> Read() {
        try {
            return File.Exists(_file)
                ? File.ReadAllLines(_file).Where(line => line.Length != 0).Distinct(PathComparer).Take(Capacity).ToList()
                : [];
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            // The list is a convenience: without it, the viewer works as before.
            return [];
        }
    }

    private void Write(List<string> paths) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
            // Written beside it and moved over it, so a viewer that stops halfway leaves the old list, not half a new one.
            var temporary = _file + ".tmp";
            File.WriteAllLines(temporary, paths);
            File.Move(temporary, _file, overwrite: true);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            // As above: the list in memory still serves this run.
        }
    }
}
