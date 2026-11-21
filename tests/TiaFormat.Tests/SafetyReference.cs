using System.Globalization;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>One value of a safety report in TIA Openness's terms: kind of object, its name, the attribute, the value.</summary>
public readonly record struct SafetyRow(string Kind, string Name, string Attribute, string Value);

/// <summary>
/// Spec 0012, FR-016: the safety report of a reference project against what TIA reported through Openness when it built
/// it, <c>generated/safety/&lt;PLC&gt;.tsv</c>. The report is written as rows in TIA's terms; only the attributes the report
/// covers are compared, and each must be on both sides with the same value.
/// </summary>
internal static class SafetyReference {
    /// <summary>The attributes of TIA's report that the safety report covers, per kind of object.</summary>
    private static readonly Dictionary<string, string[]> Compared = new(StringComparer.Ordinal) {
        ["program"] = ["PasswordSet", "CollectiveOfflineSignature", "SoftwareOfflineSignature", "HardwareOfflineSignature", "CommunicationAddressOfflineSignature"],
        ["settings"] = ["ActivationOfFChangeHistory", "SafetyModeCanBeDisabled", "SafetySystemVersion"],
        ["blocknumbers"] = ["ManagementMode", "FromFB", "ToFB", "FromFC", "ToFC", "FromDB", "ToDB"],
        ["group"] = ["FOBName", "FOBNumber", "FOBEventClass", "FOBCycleTime", "FOBPriority", "MainSafetyBlockName", "MainSafetyBlockIDbName", "InfoDbName",
            "PreProcessingName", "PostProcessingName", "MaximumCycleTime", "WarnCycleTime"],
        ["cpu"] = ["Failsafe_FCapabilityActivated", "Failsafe_CentralFSourceAddress", "Failsafe_LowerBoundForFDestinationAddresses",
            "Failsafe_UpperBoundForFDestinationAddresses", "Failsafe_DefaultFmonitoringTimeCentral"],
        ["fio"] = ["StartAddress.Input", "StartAddress.Output", "Failsafe_FSourceAddress", "Failsafe_FDestinationAddress", "Failsafe_FMonitoringtime",
            "Failsafe_ManualAssignmentFMonitoringtime", "Failsafe_FParameterSignatureWithAddresses", "Failsafe_FParameterSignatureWithoutAddresses",
            "Failsafe_FIODBNumber", "Failsafe_FIODBName"],
        ["block"] = ["Kind", "Number", "ProgrammingLanguage", "BlockOfflineSignature", "Generated"],
    };

    /// <summary>The PLCs of a reference project for which TIA reported a safety program.</summary>
    public static IReadOnlyList<string> Plcs(string project) {
        var folder = Path.Combine(ReferenceProjects.Generated(project), "safety");
        return Directory.Exists(folder) ? [.. Directory.GetFiles(folder, "*.tsv").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!] : [];
    }

    /// <summary>Every difference between the reader's report of each PLC and TIA's, for one reference project.</summary>
    public static IReadOnlyList<string> Problems(string project, out int compared) {
        compared = 0;
        var report = TiaSafety.Read(new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive(project))));
        var problems = new List<string>();
        foreach (var plc in Plcs(project)) {
            if (report.Plcs.SingleOrDefault(candidate => candidate.Name == plc)?.Program is not { } program) {
                problems.Add($"{project}: {plc}: TIA reports a safety program, the reader none");
                continue;
            }
            var ours = Rows(plc, program);
            problems.AddRange(Problems(project, plc, ours, Tia(project, plc)));
            compared += ours.Count(row => Compared[row.Kind].Contains(row.Attribute));
        }
        return problems;
    }

    /// <summary>TIA's rows of a PLC, as the exporter wrote them; a block's folder becomes whether TIA generated it.</summary>
    public static IReadOnlyList<SafetyRow> Tia(string project, string plc) {
        var rows = File.ReadLines(Path.Combine(ReferenceProjects.Generated(project), "safety", plc + ".tsv")).Skip(1)
            .Where(line => line.Length != 0)
            .Select(line => line.Split('\t'))
            .Select(columns => new SafetyRow(columns[0], columns[1], columns[2], columns.Length > 3 ? columns[3] : ""))
            .ToList();
        var fObs = rows.Where(row => row is { Kind: "group", Attribute: "FOBName" }).Select(row => row.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var folder in rows.Where(row => row is { Kind: "block", Attribute: "Folder" }).ToList()) {
            var generated = folder.Value.StartsWith("System blocks", StringComparison.Ordinal) || fObs.Contains(folder.Name);
            rows.Add(new SafetyRow("block", folder.Name, "Generated", generated ? "True" : "False"));
        }
        return rows;
    }

    /// <summary>The reader's report of one PLC as rows in TIA's terms, for the attributes it covers.</summary>
    public static IReadOnlyList<SafetyRow> Rows(string plc, TiaSafetyProgram program) {
        var rows = new List<SafetyRow>();
        void Add(string kind, string name, string attribute, object? value) {
            if (value is not null) {
                rows.Add(new SafetyRow(kind, name, attribute, Convert.ToString(value, CultureInfo.InvariantCulture)!));
            }
        }
        static string Bool(bool value) => value ? "True" : "False";

        Add("program", plc, "PasswordSet", Bool(program.IsPasswordSet));
        // TIA reports a signature it does not have as 0.
        Add("program", plc, "CollectiveOfflineSignature", program.Signatures.Collective ?? 0);
        Add("program", plc, "SoftwareOfflineSignature", program.Signatures.Software ?? 0);
        Add("program", plc, "HardwareOfflineSignature", program.Signatures.Hardware ?? 0);
        Add("program", plc, "CommunicationAddressOfflineSignature", program.Signatures.CommunicationAddress ?? 0);
        Add("settings", plc, "ActivationOfFChangeHistory", Bool(program.Settings.ChangeHistory));
        Add("settings", plc, "SafetyModeCanBeDisabled", Bool(program.Settings.SafetyModeCanBeDisabled));
        Add("settings", plc, "SafetySystemVersion", program.Settings.SystemVersion);
        Add("blocknumbers", plc, "ManagementMode", program.Settings.FixedBlockNumbers ? "FixedRange" : "FSystemManaged");
        Add("blocknumbers", plc, "FromFB", program.Settings.FbNumbers.From);
        Add("blocknumbers", plc, "ToFB", program.Settings.FbNumbers.To);
        Add("blocknumbers", plc, "FromFC", program.Settings.FcNumbers.From);
        Add("blocknumbers", plc, "ToFC", program.Settings.FcNumbers.To);
        Add("blocknumbers", plc, "FromDB", program.Settings.DbNumbers.From);
        Add("blocknumbers", plc, "ToDB", program.Settings.DbNumbers.To);

        foreach (var group in program.RuntimeGroups) {
            Add("group", group.Name, "FOBName", group.FOb?.Name);
            Add("group", group.Name, "FOBNumber", group.FOb?.Number);
            Add("group", group.Name, "FOBEventClass", group.EventClass);
            Add("group", group.Name, "FOBCycleTime", group.CycleTime);
            Add("group", group.Name, "FOBPriority", group.Priority);
            Add("group", group.Name, "MainSafetyBlockName", group.MainBlock?.Name);
            Add("group", group.Name, "MainSafetyBlockIDbName", group.MainInstance?.Name);
            Add("group", group.Name, "InfoDbName", group.InformationBlock);
            Add("group", group.Name, "PreProcessingName", group.PreProcessing);
            Add("group", group.Name, "PostProcessingName", group.PostProcessing);
            Add("group", group.Name, "MaximumCycleTime", group.MaximumCycleTime);
            Add("group", group.Name, "WarnCycleTime", group.WarningCycleTime);
        }

        Add("cpu", plc, "Failsafe_FCapabilityActivated", "True");
        Add("cpu", plc, "Failsafe_CentralFSourceAddress", program.Cpu.CentralFSourceAddress.Value);
        Add("cpu", plc, "Failsafe_LowerBoundForFDestinationAddresses", program.Cpu.LowestFDestinationAddress.Value);
        Add("cpu", plc, "Failsafe_UpperBoundForFDestinationAddresses", program.Cpu.HighestFDestinationAddress.Value);
        Add("cpu", plc, "Failsafe_DefaultFmonitoringTimeCentral", program.Cpu.DefaultMonitoringTime.Value);

        foreach (var io in program.Io) {
            var name = io.Device + "/" + io.Name;
            Add("fio", name, "StartAddress.Input", io.InputAddress);
            Add("fio", name, "StartAddress.Output", io.OutputAddress);
            Add("fio", name, "Failsafe_FSourceAddress", io.FSourceAddress.Value);
            Add("fio", name, "Failsafe_FDestinationAddress", io.FDestinationAddress.Value);
            Add("fio", name, "Failsafe_FMonitoringtime", io.MonitoringTime);
            Add("fio", name, "Failsafe_ManualAssignmentFMonitoringtime", io.MonitoringTimeByHand ? 1 : 0);
            Add("fio", name, "Failsafe_FParameterSignatureWithAddresses", io.ParameterSignature);
            Add("fio", name, "Failsafe_FParameterSignatureWithoutAddresses", io.ParameterSignatureWithoutAddresses);
            Add("fio", name, "Failsafe_FIODBNumber", io.DataBlockNumber);
            Add("fio", name, "Failsafe_FIODBName", io.DataBlockName);
        }

        foreach (var block in program.Blocks) {
            Add("block", block.Name, "Kind", block.Kind);
            Add("block", block.Name, "Number", block.Number);
            Add("block", block.Name, "ProgrammingLanguage", block.Language);
            Add("block", block.Name, "BlockOfflineSignature", block.Signature ?? 0);
            Add("block", block.Name, "Generated", Bool(block.IsGenerated));
        }
        return rows;
    }

    /// <summary>
    /// The differences between the reader's rows and TIA's, for the attributes the report covers: a value on one side
    /// only, or two values that differ. Each names the project, the PLC, the object, the attribute and both values.
    /// </summary>
    public static IReadOnlyList<string> Problems(string project, string plc, IEnumerable<SafetyRow> ours, IEnumerable<SafetyRow> tia) {
        static Dictionary<(string, string, string), string> Index(IEnumerable<SafetyRow> rows) =>
            rows.Where(row => Compared.TryGetValue(row.Kind, out var attributes) && attributes.Contains(row.Attribute))
                .ToDictionary(row => (row.Kind, row.Name, row.Attribute), row => Normal(row.Attribute, row.Value));
        var mine = Index(ours);
        var theirs = Index(tia);
        var problems = new List<string>();
        foreach (var key in mine.Keys.Union(theirs.Keys).OrderBy(key => key)) {
            var (kind, name, attribute) = key;
            var what = $"{project}: {plc} {kind} {name} {attribute}";
            if (!theirs.TryGetValue(key, out var their)) {
                problems.Add($"{what}: ours `{mine[key]}`, TIA reports none");
            } else if (!mine.TryGetValue(key, out var our)) {
                problems.Add($"{what}: TIA's `{their}`, the reader reports none");
            } else if (our != their) {
                problems.Add($"{what}: ours `{our}`, TIA's `{their}`");
            }
        }
        return problems;
    }

    /// <summary>Openness names data blocks by their kind, global or instance; the reader as TIA's block list does, DB.</summary>
    private static string Normal(string attribute, string value) => attribute == "Kind" && value is "GlobalDB" or "InstanceDB" ? "DB" : value;
}
