using System.Globalization;
using Avalonia.Platform.Storage;

namespace TiaViewer;

/// <summary>
/// The files the viewer opens: archives (<c>.zap&lt;NN&gt;</c>), projects (<c>.ap&lt;NN&gt;</c>) and global libraries (<c>.al&lt;NN&gt;</c>)
/// of every TIA Portal version, with the service pack form such as <c>zap15_1</c>, and a <c>PEData.plf</c> itself.
/// The open dialog and <c>macos/Info.plist</c> list the same extensions; a test keeps them equal.
/// </summary>
public static class TiaFileTypes {
    /// <summary>The first TIA Portal version listed.</summary>
    public const int FirstVersion = 11;

    /// <summary>
    /// The last version listed. macOS has no wildcard for an extension, so a version after this one is greyed out in its open dialog
    /// and not opened by a double-click until the list grows.
    /// </summary>
    public const int LastVersion = 30;

    /// <summary>What an archive's extension starts with, followed by the version.</summary>
    public const string ArchivePrefix = "zap";

    /// <summary>What a project's extension starts with.</summary>
    public const string ProjectPrefix = "ap";

    /// <summary>What a global library's extension starts with.</summary>
    public const string LibraryPrefix = "al";

    /// <summary>The database file inside a project or library folder, which can also be opened by itself.</summary>
    public const string DatabaseName = "PEData.plf";

    // A service pack that changed the format got its own extension, as V15.1's did.
    private static readonly string[] ServicePacks = ["", "_1"];

    /// <summary>The extensions for one kind of file, without the dot, for example <c>zap15_1</c>; in version order.</summary>
    public static IReadOnlyList<string> Extensions(string prefix) =>
        Enumerable.Range(FirstVersion, LastVersion - FirstVersion + 1)
            .SelectMany(version => ServicePacks.Select(pack => string.Create(CultureInfo.InvariantCulture, $"{prefix}{version}{pack}")))
            .ToList();

    /// <summary>Every extension of an archive, a project or a global library, without the dot.</summary>
    public static IReadOnlyList<string> AllExtensions { get; } =
        [.. Extensions(ArchivePrefix), .. Extensions(ProjectPrefix), .. Extensions(LibraryPrefix)];

    /// <summary>
    /// The open dialog's filter. Windows and Linux match a wildcard; macOS takes only whole extensions, so it gets each one.
    /// </summary>
    public static FilePickerFileType PickerType(bool wildcards) => new("TIA Portal archive, project or library") {
        Patterns = wildcards
            ? [$"*.{ArchivePrefix}*", $"*.{ProjectPrefix}*", $"*.{LibraryPrefix}*", DatabaseName]
            : [.. AllExtensions.Select(extension => "*." + extension), DatabaseName],
    };
}
