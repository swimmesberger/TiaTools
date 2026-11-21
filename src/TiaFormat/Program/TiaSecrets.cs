using System.Text.RegularExpressions;

namespace TiaFormat.Program;

/// <summary>
/// What this reader never shows: values whose name says they hold a password, key, hash or certificate, and the
/// objects of user management and certificate stores as a whole.
/// </summary>
internal static class TiaSecrets {
    private static readonly Regex Name = new(
        "password|passwd|secret|private|credential|encrypt|hash|salt|certificate|thumbprint|pki|key",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static bool IsSecretName(string name) => Name.IsMatch(name);

    /// <summary>User management (<c>Umac</c>), an HMI device's runtime users and their rights (<c>RTSecurity</c>), certificates and their stores.</summary>
    public static bool IsSecretType(string fullName) =>
        fullName.Contains("Umac", StringComparison.Ordinal) || fullName.Contains(".RTSecurity.", StringComparison.Ordinal)
        || fullName.Contains("Certificate", StringComparison.Ordinal) || fullName.Contains("Pki", StringComparison.Ordinal);
}
