using System.Globalization;

namespace TiaFormat.Program;

/// <summary>
/// Writes a project's cross references as text, one use per line, for <c>tiadump xref</c> (spec 0015, FR-011): a first
/// line saying where they come from, a header, then tab-separated columns: PLC, user, its kind, what it uses, the access,
/// the network, the line in the user's text counted from 1, and that line.
/// </summary>
public static class TiaCrossReferenceWriter {
    public static void Write(TiaProject project, TextWriter output, string? name = null, string? plc = null) {
        project.BuildIndex();
        output.Write(project.ReferencesFromText
            ? "# from the text: the archive holds no cross-reference list of TIA's that belongs to it, so a closed block's uses are not known and some may be missed\n"
            : "# from TIA's cross-reference list\n");
        output.Write("plc\tuser\tkind\tused\taccess\tnetwork\tline\ttext\n");
        foreach (var item in project.Items) {
            if (plc is not null && item.Scope[(item.Scope.IndexOf('/', StringComparison.Ordinal) + 1)..] != plc) {
                continue;
            }
            foreach (var reference in project.ReferencesFrom(item)) {
                var used = Used(reference);
                if (name is not null && reference.From.Name != name && reference.To.Name != name && reference.Target != name && used != name) {
                    continue;
                }
                var access = reference.Access ?? (reference.Kind == TiaReferenceKind.Call ? "Call" : "Use");
                output.Write(string.Create(CultureInfo.InvariantCulture,
                    $"{item.Scope[(item.Scope.IndexOf('/', StringComparison.Ordinal) + 1)..]}\t{item.Name}\t{item.Kind}\t{used}\t{access}\t{reference.Network}\t{(item.IsClosed ? "" : (reference.Line + 1).ToString(CultureInfo.InvariantCulture))}\t{reference.Text}\n"));
            }
        }
    }

    /// <summary>What is used, as TIA's list writes it: a tag by its name, a data block member as <c>"Recipe".count</c>.</summary>
    public static string Used(TiaReference reference) => reference.Target is not { } target ? reference.To.Name
        : reference.To.Table is not null ? target
        : "\"" + reference.To.Name + "\"." + target;
}
