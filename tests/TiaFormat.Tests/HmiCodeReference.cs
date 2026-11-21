using System.Globalization;
using System.Text;
using System.Xml.Linq;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>
/// Spec 0017, FR-014: an HMI device's code as the reader reads it against what TIA Portal gave for it through Openness
/// when it built a reference project, in <c>generated/&lt;HMI device&gt;/</c>: each Unified script's code
/// (<c>scripts.tsv</c>, <c>scripts/</c>), TIA's export of each global script module (<c>modules/</c>), each placed custom
/// web control (<c>controls.tsv</c>), and TIA's SimaticML export of each VB script. A custom web control's package is
/// compared with the files the sources hold for it, which TIA took as they are.
/// </summary>
internal static class HmiCodeReference {
    /// <summary>Each difference, naming the project, the device and what differs; <paramref name="compared"/> counts the objects compared.</summary>
    public static IReadOnlyList<string> Problems(string name, out int compared) {
        compared = 0;
        var report = TiaHmi.Read(new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive(name))), code: true);
        var problems = new List<string>();
        foreach (var folder in Directory.GetDirectories(ReferenceProjects.Generated(name))) {
            var device = report.Devices.FirstOrDefault(candidate => candidate.Name == Path.GetFileName(folder));
            if (device?.Code is not { } code) {
                continue;
            }
            var what = $"{name}: HMI device {device.Name}";
            compared += device.Family == TiaWinCcFamily.Unified
                ? Unified(code, folder, Path.Combine(ReferenceProjects.Folder(name), "sources", device.Name), what, problems)
                : Advanced(code, folder, what, problems);
        }
        return problems;
    }

    private static int Unified(TiaHmiCode code, string folder, string sources, string what, List<string> problems) {
        var compared = 0;
        var theirScripts = Rows(Path.Combine(folder, "scripts.tsv")).ToList();
        Expect(problems, what, "script count", Count(code.Scripts.Count), Count(theirScripts.Count));
        foreach (var row in theirScripts) {
            compared++;
            var item = row[1].Length == 0 ? null : row[1];
            var kind = row[2] == "event" ? TiaHmiScriptKind.Event : TiaHmiScriptKind.Property;
            var script = code.Scripts.FirstOrDefault(candidate => candidate.Screen == row[0] && candidate.Item == item && candidate.Kind == kind && candidate.Name == row[3]);
            if (script is null) {
                problems.Add($"{what}: {row[2]} script {row[3]} of {row[0]}/{row[1]} not read");
                continue;
            }
            Expect(problems, what, $"code of {TiaHmiWriter.Attached(script)}", script.Code, Text(Path.Combine(folder, row[4])));
        }
        var modules = Path.Combine(folder, "modules");
        var theirModules = Directory.Exists(modules) ? Directory.GetFiles(modules, "*.hmi.js") : [];
        Expect(problems, what, "global script module count", Count(code.Modules.Count), Count(theirModules.Length));
        foreach (var file in theirModules) {
            compared++;
            var moduleName = Path.GetFileName(file)[..^".hmi.js".Length];
            var module = code.Modules.FirstOrDefault(candidate => candidate.Name == moduleName);
            if (module is null) {
                problems.Add($"{what}: global script module {moduleName} not read");
                continue;
            }
            Expect(problems, what, $"global script module {moduleName}", module.Code, Text(file));
        }
        foreach (var row in Rows(Path.Combine(folder, "controls.tsv"))) {
            compared++;
            var control = code.CustomWebControls.FirstOrDefault(candidate => candidate.Guid == row[2]);
            if (control is null) {
                problems.Add($"{what}: custom web control {row[2]} not read");
                continue;
            }
            if (!control.UsedBy.Contains((row[0], row[1]))) {
                problems.Add($"{what}: custom web control {row[2]} not read as used by {row[0]}/{row[1]}");
            }
            var package = Path.Combine(sources, "custom web controls", control.Guid);
            var theirFiles = Directory.GetFiles(package, "*", SearchOption.AllDirectories);
            Expect(problems, what, $"custom web control {row[2]} file count", Count(control.Files.Count), Count(theirFiles.Length));
            foreach (var file in theirFiles) {
                compared++;
                var path = Path.GetRelativePath(package, file).Replace('\\', '/');
                var mine = control.Files.FirstOrDefault(candidate => candidate.Path == path);
                if (mine is null || !mine.Content.AsSpan().SequenceEqual(File.ReadAllBytes(file))) {
                    problems.Add($"{what}: custom web control {row[2]} file {path} {(mine is null ? "not read" : "differs")}");
                }
            }
        }
        return compared;
    }

    private static int Advanced(TiaHmiCode code, string folder, string what, List<string> problems) {
        var compared = 0;
        var theirs = Directory.GetFiles(folder, "VBSCRIPT.*.xml");
        Expect(problems, what, "VB script count", Count(code.VbScripts.Count), Count(theirs.Length));
        foreach (var file in theirs) {
            compared++;
            var attributes = XDocument.Load(file).Descendants("Hmi.RuntimeScripting.VBScript").Single().Element("AttributeList")!;
            var scriptName = (string?)attributes.Element("Name") ?? "";
            var script = code.VbScripts.FirstOrDefault(candidate => candidate.Name == scriptName);
            if (script is null) {
                problems.Add($"{what}: VB script {scriptName} not read");
                continue;
            }
            Expect(problems, what, $"VB script {scriptName} code", script.Code, Lf((string?)attributes.Element("Code")));
            Expect(problems, what, $"VB script {scriptName} type", script.ReturnType, (string?)attributes.Element("Type"));
            // TIA exports blank lines where the reader reads no pre-code.
            Expect(problems, what, $"VB script {scriptName} pre-code", script.PreCode ?? "", string.IsNullOrWhiteSpace((string?)attributes.Element("PreCode")) ? "" : Lf((string?)attributes.Element("PreCode")));
        }
        return compared;
    }

    private static void Expect(List<string> problems, string what, string field, string? ours, string? theirs) {
        if ((ours ?? "") != (theirs ?? "")) {
            problems.Add($"{what}: {field}: ours `{ours}`, TIA's `{theirs}`");
        }
    }

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>A file TIA wrote, its lines ending in LF as the reader's do.</summary>
    private static string Text(string file) => Lf(File.ReadAllText(file, Encoding.UTF8));

    private static string Lf(string? text) => (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>The rows of a tab-separated file after its header.</summary>
    private static IEnumerable<string[]> Rows(string file) =>
        File.Exists(file) ? File.ReadLines(file).Skip(1).Where(line => line.Length != 0).Select(line => line.Split('\t')) : [];
}
