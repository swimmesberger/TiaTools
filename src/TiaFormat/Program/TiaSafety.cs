using System.Globalization;
using System.Text;
using TiaFormat.Schema;
using TiaFormat.Storage;

namespace TiaFormat.Program;

/// <summary>A project's safety report: one entry per PLC of the project itself (spec 0012).</summary>
public sealed record TiaSafetyReport(IReadOnlyList<TiaSafetyPlc> Plcs);

/// <summary>One PLC; <see cref="Program"/> is null when the PLC has no safety program.</summary>
public sealed record TiaSafetyPlc(string Name, TiaSafetyProgram? Program);

/// <summary>
/// A PLC's safety program as the archive stores it in clear: its settings, whether a password is set (never more of the
/// password), its signatures, F-runtime groups, fail-safe blocks, and the F-parameters of the F-CPU and its F-I/O.
/// </summary>
public sealed record TiaSafetyProgram(
    TiaSafetySettings Settings,
    bool IsPasswordSet,
    TiaSafetySignatures Signatures,
    IReadOnlyList<TiaRuntimeGroup> RuntimeGroups,
    IReadOnlyList<TiaFailsafeBlock> Blocks,
    TiaFailsafeCpu Cpu,
    IReadOnlyList<TiaFailsafeIo> Io);

/// <summary>
/// The safety administration's settings. With <see cref="FixedBlockNumbers"/> TIA takes the numbers of the F-blocks it
/// generates from the three ranges; otherwise it manages them itself, and the ranges are what it would use.
/// </summary>
public sealed record TiaSafetySettings(
    string? SystemVersion,
    bool ChangeHistory,
    bool SafetyModeCanBeDisabled,
    bool FixedBlockNumbers,
    TiaNumberRange FbNumbers,
    TiaNumberRange FcNumbers,
    TiaNumberRange DbNumbers);

/// <summary>A range of block numbers, both ends included.</summary>
public readonly record struct TiaNumberRange(long From, long To);

/// <summary>
/// The safety program's signatures. A signature is null when the archive holds none, or zero: the program was never
/// compiled, or was changed since, as setting a password does.
/// </summary>
public sealed record TiaSafetySignatures(uint? Collective, uint? Software, uint? Hardware, uint? CommunicationAddress, DateTime? Compiled);

/// <summary>
/// One F-runtime group: the F-OB that calls it, its main safety block and that block's instance data block, and its
/// cycle times, all in µs as stored.
/// </summary>
public sealed record TiaRuntimeGroup(
    string Name,
    TiaSafetyBlockReference? FOb,
    string? EventClass,
    long? CycleTime,
    long? Priority,
    TiaSafetyBlockReference? MainBlock,
    TiaSafetyBlockReference? MainInstance,
    string? InformationBlock,
    string? PreProcessing,
    string? PostProcessing,
    long? MaximumCycleTime,
    long? WarningCycleTime);

/// <summary>A block the report names: its kind, number and name, as the program lists them.</summary>
public readonly record struct TiaSafetyBlockReference(string Kind, long? Number, string Name);

/// <summary>A fail-safe block with its F-signature; <see cref="IsGenerated"/> when TIA made it, not the user.</summary>
public sealed record TiaFailsafeBlock(string Kind, long? Number, string Name, string? Language, uint? Signature, bool IsGenerated, bool IsKnowHowProtected);

/// <summary>A value of the F-parameters: the one TIA uses, and whether the archive stores it or it is TIA's default.</summary>
public readonly record struct TiaSafetyValue(long? Value, bool IsDefault);

/// <summary>The F-CPU's F-parameters.</summary>
public sealed record TiaFailsafeCpu(
    TiaSafetyValue CentralFSourceAddress,
    TiaSafetyValue LowestFDestinationAddress,
    TiaSafetyValue HighestFDestinationAddress,
    TiaSafetyValue DefaultMonitoringTime);

/// <summary>
/// One F-I/O, named by its device and its own name as the device list names them (spec 0010), with its F-parameters:
/// addresses, monitoring time in ms, and F-parameter signatures.
/// </summary>
public sealed record TiaFailsafeIo(
    string Device,
    string Name,
    long? InputAddress,
    long? OutputAddress,
    TiaSafetyValue FSourceAddress,
    TiaSafetyValue FDestinationAddress,
    long? MonitoringTime,
    bool MonitoringTimeByHand,
    long? ParameterSignature,
    long? ParameterSignatureWithoutAddresses,
    long? DataBlockNumber,
    string? DataBlockName);

/// <summary>
/// Reads a project's safety programs (spec 0012, docs/FORMAT.md section 19). Never reads a password beyond whether one is
/// set, and nothing encrypted.
/// </summary>
public static class TiaSafety {
    private const string Model = "Siemens.Simatic.Safety.Model.";
    private const string Core = "Siemens.Automation.ObjectFrame.ICoreAttributes";
    private const string Settings = Model + "IFailsafeProgramSettings";
    private const string Compilation = Model + "IFailsafeCompilationInfo";
    private const string Group = Model + "IRuntimegroupInfo";
    private const string Module = Model + "IFModuleInfo";

    /// <summary>The F-destination address TIA uses when an F-I/O stores none.</summary>
    public const long DefaultFDestinationAddress = 65534;

    /// <summary>The F-CPU's default F-monitoring time in ms when the PLC stores none.</summary>
    public const long DefaultMonitoringTime = 150;

    /// <summary>The report of every PLC of the project, or of the one named <paramref name="plc"/> only.</summary>
    public static TiaSafetyReport Read(TiaProgram program, string? plc = null) {
        var db = program.Database;
        var s = db.Schema;
        var name = s.Attribute(Core, "Name");
        var plcType = s.FindObjectType("Siemens.Simatic.HwConfiguration.Model.S7ControllerTargetData")?.Id;
        var infoType = s.FindObjectType(Model + "FailsafeInfo")?.Id;
        var target = s.Relation("Target");

        var plcs = new List<(TiaFrame Frame, string Name)>();
        var infos = new Dictionary<ulong, TiaFrame>();
        foreach (ref readonly var frame in db.Frames) {
            if (frame.IsDeleted) {
                continue;
            }
            if (frame.TypeId == plcType) {
                if (program.Environment(frame).Kind == "ProjectData" && db.ReadString(frame, name) is { } plcName && (plc is null || plcName == plc)) {
                    plcs.Add((frame, plcName));
                }
            } else if (frame.TypeId == infoType && db.TryGetTarget(frame, target, out var owner)) {
                infos.TryAdd(owner.ObjectId, frame);
            }
        }
        var blocks = program.Blocks.Where(block => !block.IsStub).ToDictionary(block => block.Frame.ObjectId);
        return new TiaSafetyReport([.. plcs.OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry => new TiaSafetyPlc(entry.Name, infos.TryGetValue(entry.Frame.ObjectId, out var info) ? ReadProgram(program, blocks, entry.Frame, info) : null))]);
    }

    /// <summary>Whether a PLC of the project has a safety program, without reading it: what the viewer's tree needs.</summary>
    public static HashSet<string> PlcsWithSafetyProgram(TiaProgram program) {
        var db = program.Database;
        var infoType = db.Schema.FindObjectType(Model + "FailsafeInfo")?.Id;
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (infoType is null) {
            return result;
        }
        foreach (ref readonly var frame in db.Frames) {
            if (frame.TypeId == infoType && !frame.IsDeleted && program.Environment(frame).Kind == "ProjectData" && program.PlcName(frame) is { } plc) {
                result.Add(plc);
            }
        }
        return result;
    }

    private static TiaSafetyProgram ReadProgram(TiaProgram program, Dictionary<ulong, TiaBlock> blocks, TiaFrame plc, TiaFrame info) {
        var db = program.Database;
        var s = db.Schema;
        TiaFrame? settings = Single(db, info, "FailsafeInfoToFailsafeProgramSettings");
        TiaFrame? compilation = Single(db, info, "FailsafeInfoToCompilationInfos");

        long? Int(TiaFrame? frame, string set, string attribute) => frame is { } f ? db.ReadInteger(f, s.Attribute(set, attribute)) : null;
        bool Flag(TiaFrame? frame, string set, string attribute) => frame is { } f && (db.ReadOptionalBoolean(f, s.Attribute(set, attribute)) ?? false);
        TiaNumberRange Range(string from, string to) => new(Int(settings, Settings, from) ?? 0, Int(settings, Settings, to) ?? 0);

        var programSettings = new TiaSafetySettings(
            settings is { } st ? db.ReadString(st, s.Attribute(Settings, "SafetySystemVersion")) : null,
            Flag(settings, Settings, "ActivateFChangelog"),
            Flag(settings, Settings, "SafetyModeCanBeDisabled"),
            Int(settings, Settings, "AutomaticOrManual") == 1,
            Range("FunctionblockFrom", "FunctionblockTo"),
            Range("FunctionsFrom", "FunctionsTo"),
            Range("DatablockFrom", "DatablockTo"));

        // Whether a password is set is whether its value is empty; the value itself is never shown or used.
        var passwordSet = settings is { } ps && !string.IsNullOrEmpty(db.ReadString(ps, s.Attribute(Settings, "FProgramPasswordPlus")));

        uint? Signature(long? value) => value is { } v && v != 0 ? unchecked((uint)v) : null;
        var ticks = Int(compilation, Compilation, "TimeLastGen");
        var signatures = new TiaSafetySignatures(
            Signature(Int(compilation, Compilation, "OverallSignature")),
            Signature(settings is { } sw ? db.ReadExpandoInteger(sw, "FunctionalSoftwareSignature") : null),
            Signature(settings is { } hw ? db.ReadExpandoInteger(hw, "FunctionalHardwareSignature") : null),
            Signature(settings is { } ca ? db.ReadExpandoInteger(ca, "CommunicationAddressSignature") : null),
            ticks is > 0 and < 3155378975999999999 ? new DateTime(ticks.Value, DateTimeKind.Utc) : null);

        var groups = new List<(long Id, TiaRuntimeGroup Group)>();
        var fObs = new HashSet<ulong>();
        foreach (var group in Many(db, info, "FailsafeInfoToRuntimegroupInfos")) {
            TiaFrame? fOb = Single(db, group, "RuntimegroupInfoToMainSafetyCallers");
            TiaFrame? handled = fOb is { } ob ? Single(db, ob, "HandledEvent") : null;
            if (fOb is { } caller) {
                fObs.Add(caller.ObjectId);
            }
            var fObBlock = fOb is { } fo && blocks.TryGetValue(fo.ObjectId, out var b) ? b : null;
            groups.Add((Int(group, Group, "RuntimegroupID") ?? long.MaxValue, new TiaRuntimeGroup(
                db.ReadString(group, s.Attribute(Core, "Name")) ?? "",
                Reference(blocks, fOb),
                fObBlock?.EventClass is { } eventClass ? Words(eventClass) : null,
                Int(handled, "Siemens.Simatic.Lang.Model.Blocks.ICyclicInterruptEventData", "CyclicTime"),
                Int(handled, "Siemens.Simatic.Lang.Model.Blocks.IOrganizationBlockEventDataAttributes", "Priority"),
                Reference(blocks, Single(db, group, "RuntimegroupInfoToMainSafety")),
                Reference(blocks, Single(db, group, "RuntimegroupInfoToMainSafetyInstanceDataBlocks")),
                NonEmpty(db.ReadString(group, s.Attribute(Group, "Control1IDBSymbol"))),
                NonEmpty(db.ReadString(group, s.Attribute(Group, "PreProcessorFCName"))),
                NonEmpty(db.ReadString(group, s.Attribute(Group, "PostProcessorFCName"))),
                Int(group, Group, "MaxCycleTime"),
                Int(group, Group, "WarningCycletime"))));
        }

        var blockInfo = s.Relation("GeneralBlockSourceDataToBlockInfo");
        var signature = s.Attribute(Model + "IFBlockInfoExtension", "FunctionalSignature");
        var target = s.Relation("Target");
        var failsafeBlocks = new List<TiaFailsafeBlock>();
        foreach (var block in blocks.Values) {
            if (!db.TryGetTarget(block.Frame, target, out var owner) || owner.ObjectId != plc.ObjectId || program.Environment(block.Frame).Kind != "ProjectData"
                || !db.TryGetTarget(block.Frame, blockInfo, out var fBlock)) {
                continue;
            }
            var generated = fObs.Contains(block.Frame.ObjectId) || program.FolderPath(block.Frame).Split('/').Contains("SystemBlocksFolder");
            failsafeBlocks.Add(new TiaFailsafeBlock(block.Kind, block.Number, block.Name, block.Language, Signature(db.ReadInteger(fBlock, signature)), generated, block.IsKnowHowProtected));
        }
        failsafeBlocks.Sort((a, b) => a.IsGenerated != b.IsGenerated ? a.IsGenerated.CompareTo(b.IsGenerated)
            : string.CompareOrdinal(a.Kind, b.Kind) is var kind and not 0 ? kind
            : (a.Number ?? long.MaxValue).CompareTo(b.Number ?? long.MaxValue) is var number and not 0 ? number
            : string.CompareOrdinal(a.Name, b.Name));

        TiaSafetyValue Cpu(string key, long fallback) => db.ReadExpandoInteger(plc, key) is { } value ? new(value, false) : new(fallback, true);
        var cpu = new TiaFailsafeCpu(
            Cpu("Failsafe_CentralSourceAddress", 1),
            Cpu("Failsafe_DestinationAddress_LowerBound", 1),
            Cpu("Failsafe_DestinationAddress_UpperBound", 99),
            Cpu("Failsafe_Central_F_WD_Time", DefaultMonitoringTime));

        var io = new List<TiaFailsafeIo>();
        foreach (var module in Many(db, info, "FailsafeInfoToFModuleInfos")) {
            TiaFrame? item = Single(db, module, "FModuleInfoToDeviceItemData");
            TiaFrame? device = item is { } i ? Single(db, i, "LogicalContainer") : null;
            long? Key(string key) => item is { } it ? db.ReadExpandoInteger(it, key) : null;
            io.Add(new TiaFailsafeIo(
                device is { } d ? db.ReadString(d, s.Attribute(Core, "Name")) ?? "" : "",
                item is { } named ? db.ReadString(named, s.Attribute(Core, "Name")) ?? "" : "",
                Int(module, Module, "InputBaseAddress"),
                Int(module, Module, "OutputBaseAddress"),
                Key("Failsafe_F_Source_Add") is { } source ? new(source, false) : new(cpu.CentralFSourceAddress.Value, true),
                Key("Failsafe_F_Dest_Add") is { } destination ? new(destination, false) : new(DefaultFDestinationAddress, true),
                Int(module, Module, "MonitoringTime") ?? Key("Failsafe_F_WD_Time"),
                Key("Failsafe_F_WD_Time_Setting") == 1,
                Key("Failsafe_F_Par_CRC") ?? Int(module, Module, "ModuleCRC"),
                Key("Failsafe_F_Par_CRC_WithoutAddresses"),
                Int(module, Module, "IDBNr"),
                NonEmpty(db.ReadString(module, s.Attribute(Module, "StoredNameStorage")))));
        }
        io.Sort((a, b) => string.CompareOrdinal(a.Device, b.Device) is var device and not 0 ? device : string.CompareOrdinal(a.Name, b.Name));

        return new TiaSafetyProgram(programSettings, passwordSet, signatures, [.. groups.OrderBy(entry => entry.Id).Select(entry => entry.Group)], failsafeBlocks, cpu, io);
    }

    private static TiaSafetyBlockReference? Reference(Dictionary<ulong, TiaBlock> blocks, TiaFrame? frame) =>
        frame is { } f && blocks.TryGetValue(f.ObjectId, out var block) ? new TiaSafetyBlockReference(block.Kind, block.Number, block.Name) : null;

    private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    /// <summary><c>CyclicInterrupt</c> as TIA writes it: <c>Cyclic interrupt</c>.</summary>
    internal static string Words(string name) {
        var text = new StringBuilder(name.Length + 4);
        foreach (var c in name) {
            if (char.IsUpper(c) && text.Length != 0) {
                text.Append(' ').Append(char.ToLowerInvariant(c));
            } else {
                text.Append(c);
            }
        }
        return text.ToString();
    }

    private static TiaFrame? Single(TiaDatabase db, in TiaFrame frame, string relation) =>
        db.TryGetTarget(frame, db.Schema.Relation(relation), out var found) ? found : null;

    private static List<TiaFrame> Many(TiaDatabase db, in TiaFrame frame, string relation) {
        var handle = db.Schema.Relation(relation);
        var result = new List<TiaFrame>();
        foreach (var r in db.EnumerateRelations(frame)) {
            if (handle.Matches(r.RelationId) && db.TryGetFrame(r.Target, out var target)) {
                result.Add(target);
            }
        }
        return result;
    }

    /// <summary>A signature as TIA's safety administration shows it: eight hexadecimal digits.</summary>
    public static string Hex(uint value) => value.ToString("X8", CultureInfo.InvariantCulture);
}
