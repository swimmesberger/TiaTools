namespace TiaFormat.Program;

/// <summary>A project's HMI devices, those of the project itself (spec 0011).</summary>
public sealed record TiaHmiReport(IReadOnlyList<TiaHmiDevice> Devices);

/// <summary>Which WinCC an HMI device belongs to. Tags, connections and alarms are stored alike in both; screens are not.</summary>
public enum TiaWinCcFamily : byte {
    /// <summary>Comfort and Basic panels and WinCC RT Advanced.</summary>
    ComfortAdvanced,

    /// <summary>WinCC Unified: Unified Comfort panels and the Unified PC runtime.</summary>
    Unified,
}

/// <summary>
/// An HMI device: its HMI application, by the name TIA gives the runtime, with its WinCC family and device type, and
/// what it holds.
/// </summary>
public sealed record TiaHmiDevice(
    string Name,
    TiaWinCcFamily Family,
    string? DeviceType,
    IReadOnlyList<TiaHmiConnection> Connections,
    IReadOnlyList<TiaHmiTagTable> TagTables,
    IReadOnlyList<TiaHmiAlarm> Alarms,
    IReadOnlyList<TiaHmiScreen> Screens) {
    /// <summary>The family as TIA names it.</summary>
    public string FamilyName => Family == TiaWinCcFamily.Unified ? "Unified" : "Comfort/Advanced";
}

/// <summary>
/// An HMI connection: the PLC it leads to, and the communication driver as stored (<c>S7_1500_OMS</c> for an S7-1200 or
/// S7-1500 over its own protocol).
/// </summary>
public sealed record TiaHmiConnection(string Name, string? Plc, string? Driver, bool IsIntegrated) {
    /// <summary>
    /// The driver as TIA names it, from TIA V21's communication channel descriptions (format page, section 24); the stored
    /// id for one not listed there.
    /// </summary>
    public string? DriverName => Driver is null ? null : DriverNames.GetValueOrDefault(Driver, Driver);

    private static readonly Dictionary<string, string> DriverNames = new(StringComparer.Ordinal) {
        ["S7_1500"] = "SIMATIC S7 1500",
        ["S7_1500_OMS"] = "SIMATIC S7 1500",
        ["S7_1200_OMS"] = "SIMATIC S7 1200",
        ["SMART_S7_1200"] = "SIMATIC S7 1200",
        ["SMART_S7_1200_OMS"] = "SIMATIC S7 1200",
        ["SMART_S7_1500_OMS"] = "SIMATIC S7 1500",
        ["ILRT_S7_1500_OMS"] = "SIMATIC S7 1200/1500",
        ["ILRT_S7_300400"] = "SIMATIC S7 300/400",
        ["SIMATIC S7 PROTOCOL SUITE"] = "SIMATIC S7 300/400",
        ["SIMATIC S7 300/400 MPI"] = "SIMATIC S7 300/400",
        ["OPC_UA"] = "OPC UA",
        ["ILRT_OPC_UA"] = "OPC UA",
        ["HmiInternal"] = "Internal communication",
        ["Hmi_ILRT_Internal"] = "Internal communication",
    };
}

/// <summary>An HMI tag table, with its folder path and its HMI tags in stored order.</summary>
public sealed record TiaHmiTagTable(string Name, string Folder, IReadOnlyList<TiaHmiTag> Tags);

/// <summary>
/// An HMI tag. <see cref="Connection"/> is null for an internal tag. <see cref="PlcLink"/> is the PLC tag or data block
/// member it reads; without one, an external tag reads <see cref="Address"/>.
/// </summary>
public sealed record TiaHmiTag(string Name, string? DataType, string? Connection, string? AcquisitionCycle, string? Address, TiaHmiPlcLink? PlcLink) {
    /// <summary>An internal tag: read over no connection.</summary>
    public bool IsInternal => Connection is null;
}

/// <summary>
/// What an HMI tag reads in its PLC: a PLC tag, or a data block and the path of a member in it. A member whose path the
/// reader cannot resolve has <see cref="Member"/> null and <see cref="IsResolved"/> false.
/// </summary>
public sealed record TiaHmiPlcLink(string? Plc, string Name, bool IsDataBlock, string? Member, bool IsResolved) {
    /// <summary>
    /// As a source names it: <c>"Tank_Level"</c>, <c>"Mixer".Recipe.Speed</c>, <c>"Mixer"</c> for the whole block, or
    /// <c>"Mixer".?</c> when unresolved.
    /// </summary>
    public string Display => !IsDataBlock || Member == "" ? $"\"{Name}\"" : $"\"{Name}\".{(IsResolved ? Member : "?")}";
}

/// <summary>A discrete HMI alarm, raised by a bit, or an analog one, raised by a limit.</summary>
public enum TiaHmiAlarmKind : byte {
    Discrete,
    Analog,
}

/// <summary>
/// An HMI alarm. A discrete alarm has <see cref="Bit"/>; an analog one has <see cref="LimitMode"/> and <see cref="Limit"/>.
/// <see cref="Text"/> is the event text without TIA's rich text markup.
/// </summary>
public sealed record TiaHmiAlarm(
    TiaHmiAlarmKind Kind,
    string Name,
    long? Number,
    string? AlarmClass,
    string? TriggerTag,
    long? Bit,
    string? LimitMode,
    string? Limit,
    string? Text);

/// <summary>A screen, with its number and size where stored, and its screen items in stored order.</summary>
public sealed record TiaHmiScreen(string Name, string Folder, long? Number, long? Width, long? Height, IReadOnlyList<TiaHmiScreenItem> Items);

/// <summary>A screen item: its type as TIA names it (<c>IOField</c>, <c>TextField</c>, <c>HmiIOField</c>, ...), geometry, and the HMI tags it is bound to.</summary>
public sealed record TiaHmiScreenItem(string Name, string Type, long? Left, long? Top, long? Width, long? Height, IReadOnlyList<string> Tags);
