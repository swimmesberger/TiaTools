using System.Globalization;
using TiaFormat.Hardware;
using TiaFormat.Program;
using TiaFormat.Storage;

namespace TiaFormat.Tests;

/// <summary>One fact about the hardware: what it is about (<c>device</c>, <c>item</c>, <c>interface</c>, <c>subnet</c>, <c>iosystem</c>), its path, which attribute, the value.</summary>
internal readonly record struct HardwareRow(string Kind, string Path, string Attribute, string Value);

/// <summary>
/// Spec 0010: the reader's hardware and TIA's report of it (<c>generated/hardware.tsv</c>, written through Openness when the
/// reference project was built), both as <see cref="HardwareRow"/>s in one vocabulary, and their differences.
/// </summary>
internal static class HardwareReports {
    private const string Separator = TiaHardware.PathSeparator;

    public static string Report(string project) => Path.Combine(ReferenceProjects.Generated(project), "hardware.tsv");

    public static TiaHardwareReport Read(string project) => TiaHardware.Read(new TiaProgram(TiaDatabase.Open(ReferenceProjects.Archive(project))));

    /// <summary>What the reader lists, as rows.</summary>
    public static List<HardwareRow> Ours(TiaHardwareReport hardware) {
        var rows = new List<HardwareRow>();
        foreach (var device in hardware.Devices) {
            rows.Add(new("device", device.Name, "group", device.Group));
            Add(rows, "device", device.Name, "type", device.TypeName);
            foreach (var constant in device.Constants) {
                Add(rows, "device", device.Name, "hwid", constant.HardwareIdentifier?.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var item in device.AllItems) {
                rows.Add(new("item", item.Path, "listed", ""));
                Add(rows, "item", item.Path, "slot", item.Slot?.ToString(CultureInfo.InvariantCulture));
                Add(rows, "item", item.Path, "order", item.OrderNumber);
                Add(rows, "item", item.Path, "firmware", item.Firmware);
                Add(rows, "item", item.Path, "type", item.TypeName);
                Add(rows, "item", item.Path, "label", item.Label);
                foreach (var constant in item.Constants) {
                    Add(rows, "item", item.Path, "hwid", constant.HardwareIdentifier?.ToString(CultureInfo.InvariantCulture));
                }
                foreach (var address in item.IoAddresses) {
                    rows.Add(new("item", item.Path, "address", string.Create(CultureInfo.InvariantCulture, $"{address.Kind} {address.StartByte} {address.LengthInBits}")));
                }
                foreach (var node in item.InterfaceAddresses) {
                    var path = item.Path + Separator + node.Name;
                    Add(rows, "interface", path, "subnet", node.Subnet);
                    if (node.Kind == TiaSubnetKind.Profibus) {
                        Add(rows, "interface", path, "profibus", node.ProfibusAddress?.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }
                    Add(rows, "interface", path, "ip", node.IpAddress);
                    Add(rows, "interface", path, "mask", node.SubnetMask);
                    rows.Add(new("interface", path, "ipelsewhere", node.IpAddressSetElsewhere ? "True" : "False"));
                    Add(rows, "interface", path, "devicename", node.DeviceName);
                    Add(rows, "interface", path, "generated", node.DeviceNameGenerated is { } generated ? (generated ? "True" : "False") : null);
                }
            }
        }
        foreach (var subnet in hardware.Subnets) {
            rows.Add(new("subnet", subnet.Name, "kind", subnet.Kind.ToString()));
            rows.AddRange(subnet.Interfaces.Select(path => new HardwareRow("subnet", subnet.Name, "interface", path)));
        }
        foreach (var system in hardware.IoSystems) {
            Add(rows, "iosystem", system.Name, "number", system.Number?.ToString(CultureInfo.InvariantCulture));
            Add(rows, "iosystem", system.Name, "subnet", system.Subnet);
            Add(rows, "iosystem", system.Name, "controller", system.Controller);
            rows.AddRange(system.IoDevices.Select(path => new HardwareRow("iosystem", system.Name, "iodevice", path)));
            foreach (var constant in system.Constants) {
                Add(rows, "iosystem", system.Name, "hwid", constant.HardwareIdentifier?.ToString(CultureInfo.InvariantCulture));
            }
        }
        return rows;
    }

    /// <summary>TIA's report, as rows of the same vocabulary; what the reader does not cover is left out.</summary>
    public static List<HardwareRow> Theirs(string file) {
        var raw = File.ReadLines(file).Skip(1).Select(line => line.Split('\t')).Where(fields => fields.Length == 4).ToList();
        var rows = new List<HardwareRow>();
        var nodeTypes = raw.Where(f => f[0] == "node" && f[2] == "NodeType").ToDictionary(f => f[1], f => f[3]);
        var addresses = new Dictionary<string, Dictionary<string, string>>();
        foreach (var f in raw) {
            var (kind, path, attribute, value) = (f[0], f[1], f[2], f[3]);
            switch (kind, attribute) {
                case ("device", "group"):
                    rows.Add(new("device", path, "group", value));
                    break;
                case ("device", "TypeName"):
                    Add(rows, "device", path, "type", value);
                    break;
                case ("item", "Name"):
                    rows.Add(new("item", path, "listed", ""));
                    break;
                case ("item", "PositionNumber"):
                    Add(rows, "item", path, "slot", value);
                    break;
                case ("item", "OrderNumber"):
                    Add(rows, "item", path, "order", value);
                    break;
                case ("item", "FirmwareVersion"):
                    Add(rows, "item", path, "firmware", value);
                    break;
                case ("item", "TypeName"):
                    Add(rows, "item", path, "type", value);
                    break;
                case ("item", "Label"):
                    Add(rows, "item", path, "label", value);
                    break;
                case ("item" or "device" or "iosystem", "HwIdentifier"):
                    Add(rows, kind, path, "hwid", value);
                    break;
                case ("address", _):
                    (addresses.TryGetValue(path, out var fields) ? fields : addresses[path] = [])[attribute] = value;
                    break;
                case ("node", "subnet"):
                    Add(rows, "interface", path, "subnet", value);
                    break;
                case ("node", "Address"):
                    Add(rows, "interface", path, nodeTypes.GetValueOrDefault(path) == "Profibus" ? "profibus" : "ip", value);
                    break;
                case ("node", "SubnetMask"):
                    Add(rows, "interface", path, "mask", value);
                    break;
                case ("node", "IpProtocolSelection"):
                    rows.Add(new("interface", path, "ipelsewhere", value == "OtherPath" ? "True" : "False"));
                    break;
                case ("node", "PnDeviceName"):
                    Add(rows, "interface", path, "devicename", value);
                    break;
                case ("node", "PnDeviceNameAutoGeneration"):
                    Add(rows, "interface", path, "generated", value);
                    break;
                case ("subnet", "NetType"):
                    rows.Add(new("subnet", path, "kind", value));
                    break;
                case ("subnet", "node"):
                    rows.Add(new("subnet", path, "interface", value[..value.LastIndexOf(Separator, StringComparison.Ordinal)]));
                    break;
                case ("iosystem", "Number"):
                    Add(rows, "iosystem", path, "number", value);
                    break;
                case ("iosystem", "subnet"):
                    Add(rows, "iosystem", path, "subnet", value);
                    break;
                case ("iosystem", "iodevice"):
                    rows.Add(new("iosystem", path, "iodevice", value));
                    break;
                case ("interface", "controls") when value.Length != 0:
                    Add(rows, "iosystem", value, "controller", path);
                    break;
            }
        }
        foreach (var (path, fields) in addresses) {
            // A start of -1 is no address: the device is in no IO system, so no controller's process image holds it.
            if (fields.GetValueOrDefault("IoType") is "Input" or "Output" && fields.GetValueOrDefault("StartAddress") != "-1") {
                rows.Add(new("item", path[..path.LastIndexOf(Separator, StringComparison.Ordinal)], "address", $"{fields["IoType"]} {fields["StartAddress"]} {fields["Length"]}"));
            }
        }
        return rows;
    }

    /// <summary>
    /// Each fact on which the two sides differ, by kind, path and attribute, with both values: what only one side has
    /// reads "none" on the other.
    /// </summary>
    public static List<string> Problems(string project, IEnumerable<HardwareRow> ours, IEnumerable<HardwareRow> theirs) {
        static Dictionary<(string, string, string), List<string>> Group(IEnumerable<HardwareRow> rows) =>
            rows.GroupBy(row => (row.Kind, row.Path, row.Attribute))
                .ToDictionary(group => group.Key, group => group.Select(row => row.Value).Order(StringComparer.Ordinal).ToList());
        var mine = Group(ours);
        var tias = Group(theirs);
        var problems = new List<string>();
        foreach (var key in mine.Keys.Union(tias.Keys).OrderBy(key => key.Item1, StringComparer.Ordinal).ThenBy(key => key.Item2, StringComparer.Ordinal).ThenBy(key => key.Item3, StringComparer.Ordinal)) {
            var a = mine.TryGetValue(key, out var x) ? string.Join(", ", x) : "none";
            var b = tias.TryGetValue(key, out var y) ? string.Join(", ", y) : "none";
            if (a != b) {
                problems.Add($"{project}: {key.Item1} {key.Item2}, {key.Item3}: ours `{a}`, TIA's `{b}`");
            }
        }
        return problems;
    }

    private static void Add(List<HardwareRow> rows, string kind, string path, string attribute, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            rows.Add(new(kind, path, attribute, value));
        }
    }
}
