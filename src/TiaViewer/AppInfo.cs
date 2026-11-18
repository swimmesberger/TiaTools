using System.Reflection;

namespace TiaViewer;

/// <summary>What the About window says about the app.</summary>
public static class AppInfo {
    /// <summary>The app's name, as menus and windows show it.</summary>
    public const string Name = "TIA Viewer";

    /// <summary>Where the source is.</summary>
    public static readonly Uri Repository = new("https://github.com/swimmesberger/TiaTools");

    /// <summary>The version this build was given, such as <c>0.1.0</c>.</summary>
    public static string Version => ShortVersion(Informational());

    /// <summary>The version and, when the build knew it, the commit it was built from, such as <c>0.1.0 (cba57be)</c>.</summary>
    public static string VersionWithCommit => WithCommit(Informational());

    /// <summary>The part of an informational version before the build metadata: <c>0.1.0+cba57be…</c> gives <c>0.1.0</c>.</summary>
    public static string ShortVersion(string informational) => informational.Split('+', 2)[0];

    /// <summary>An informational version for people: <c>0.1.0+cba57be…</c> gives <c>0.1.0 (cba57be)</c>.</summary>
    public static string WithCommit(string informational) {
        var parts = informational.Split('+', 2);
        return parts.Length == 2 && parts[1].Length != 0 ? $"{parts[0]} ({parts[1][..Math.Min(7, parts[1].Length)]})" : parts[0];
    }

    private static string Informational() =>
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
}
