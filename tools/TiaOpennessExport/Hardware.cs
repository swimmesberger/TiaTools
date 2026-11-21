using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;

namespace TiaOpennessExport;

/// <summary>
/// Builds a reference project's invented hardware from <c>sources/hardware.tsv</c> (spec 0010): one row per subnet,
/// device, module, interface, IO system and IO device, applied in file order. Every row TIA refuses is reported with TIA's
/// error, and counted, so one run names them all and the committed files stay as they were.
/// </summary>
internal static class HardwareBuilder {
    public static int Build(TiaPortal portal, Project project, string file) {
        var refused = 0;
        var number = 0;
        foreach (var line in File.ReadAllLines(file, Encoding.UTF8)) {
            number++;
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }
            var fields = line.Split('\t');
            try {
                Apply(portal, project, fields);
                Console.WriteLine($"hardware {string.Join(" ", fields.Where(field => field.Length != 0))}");
            } catch (Exception e) when (e is EngineeringException or InvalidOperationException or FormatException or IndexOutOfRangeException) {
                refused++;
                Console.WriteLine($"refused hardware.tsv:{number}: {e.Message.Replace(Environment.NewLine, " ")}");
            }
        }
        return refused;
    }

    private static void Apply(TiaPortal portal, Project project, string[] fields) {
        switch (fields[0]) {
            case "subnet":
                project.Subnets.Create(fields[2] == "Profibus" ? "System:Subnet.Profibus" : "System:Subnet.Ethernet", fields[1]);
                break;
            case "device": {
                var type = TypeIdentifier(portal, fields[2], Field(fields, 3));
                var group = Field(fields, 4);
                var devices = group.Length == 0 ? project.Devices : Group(project, group).Devices;
                devices.CreateWithItem(type, fields[1], fields[1]);
                break;
            }
            case "module": {
                var device = FindDevice(project, fields[1]);
                var slot = int.Parse(fields[2], CultureInfo.InvariantCulture);
                var type = TypeIdentifier(portal, fields[3], Field(fields, 4));
                var name = fields[5];
                var rack = All(device.DeviceItems).FirstOrDefault(item => item.CanPlugNew(type, name, slot))
                    ?? throw new InvalidOperationException($"{fields[1]} has no place for {type} in slot {slot}.");
                var module = rack.PlugNew(type, name, slot);
                foreach (var start in fields.Skip(6).Where(field => field.Length != 0)) {
                    var ioType = start.StartsWith("I=", StringComparison.Ordinal) ? AddressIoType.Input
                        : start.StartsWith("Q=", StringComparison.Ordinal) ? AddressIoType.Output
                        : throw new FormatException($"A start address is I=<byte> or Q=<byte>, not {start}.");
                    var address = All(new[] { module }).SelectMany(item => item.Addresses).FirstOrDefault(candidate => candidate.IoType == ioType)
                        ?? throw new InvalidOperationException($"{name} has no {ioType} address.");
                    address.StartAddress = int.Parse(start.Substring(2), CultureInfo.InvariantCulture);
                }
                break;
            }
            case "interface": {
                var networkInterface = Interface(project, fields[1], fields[2]);
                var node = networkInterface.Nodes.First();
                node.ConnectToSubnet(project.Subnets.Find(fields[3]) ?? throw new InvalidOperationException($"No subnet {fields[3]}."));
                if (fields.Length == 5) {
                    // A PROFIBUS address, in whichever integer type TIA keeps it.
                    var current = node.GetAttribute("Address");
                    node.SetAttribute("Address", Convert.ChangeType(fields[4], current?.GetType() ?? typeof(int), CultureInfo.InvariantCulture));
                } else {
                    node.SetAttribute("Address", fields[4]);
                    node.SetAttribute("SubnetMask", fields[5]);
                    var generated = fields[6] == "auto";
                    node.SetAttribute("PnDeviceNameAutoGeneration", generated);
                    if (!generated) {
                        node.SetAttribute("PnDeviceName", fields[6]);
                    }
                }
                break;
            }
            case "ipelsewhere": {
                // "IP address is set directly at the device": the project then holds no IP address for the interface.
                var node = Interface(project, fields[1], fields[2]).Nodes.First();
                try {
                    node.SetAttribute("IpProtocolSelection", IpProtocolSelection.OtherPath);
                } catch (EngineeringException e) {
                    var writable = node.GetAttributeInfos().Where(info => info.AccessMode.ToString().Contains("Write")).Select(info => info.Name);
                    throw new InvalidOperationException($"{e.Message} Writable here: {string.Join(", ", writable)}.");
                }
                break;
            }
            case "iosystem": {
                var controller = Interface(project, fields[1], fields[2]).IoControllers.FirstOrDefault()
                    ?? throw new InvalidOperationException($"{fields[1]}/{fields[2]} is no IO controller.");
                var system = controller.CreateIoSystem(fields[3]);
                system.SetAttribute("Number", int.Parse(fields[4], CultureInfo.InvariantCulture));
                break;
            }
            case "iodevice": {
                var connector = Interface(project, fields[1], fields[2]).IoConnectors.FirstOrDefault()
                    ?? throw new InvalidOperationException($"{fields[1]}/{fields[2]} cannot be an IO device.");
                var system = project.Subnets.SelectMany(subnet => subnet.IoSystems).FirstOrDefault(candidate => candidate.Name == fields[3])
                    ?? throw new InvalidOperationException($"No IO system {fields[3]}.");
                connector.ConnectToIoSystem(system);
                break;
            }
            default:
                throw new FormatException($"Unknown row {fields[0]}.");
        }
    }

    private static string Field(string[] fields, int index) => index < fields.Length ? fields[index] : "";

    /// <summary>The catalog's type identifier of an order number: of that firmware if one is given, else the newest.</summary>
    private static string TypeIdentifier(TiaPortal portal, string orderNumber, string firmware) {
        var prefix = "OrderNumber:" + orderNumber + "/";
        var entries = portal.HardwareCatalog.Find(orderNumber)
            .Select(entry => entry.TypeIdentifier)
            .Where(type => type.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        var chosen = firmware.Length != 0
            ? entries.FirstOrDefault(type => type == prefix + firmware)
            : entries.OrderBy(type => ParseVersion(type.Substring(prefix.Length))).LastOrDefault();
        if (chosen is null) {
            var near = orderNumber.Length > 5 ? portal.HardwareCatalog.Find(orderNumber.Substring(0, orderNumber.Length - 5)).Select(entry => entry.TypeIdentifier).Take(20) : [];
            throw new InvalidOperationException($"The catalog has no {orderNumber} {firmware}; it has {string.Join(", ", entries)}; near it: {string.Join(", ", near)}.");
        }
        Console.WriteLine($"  {orderNumber} {firmware}: {chosen}");
        return chosen;
    }

    private static Version ParseVersion(string text) {
        var match = Regex.Match(text, @"\d+(\.\d+)*");
        return match.Success && Version.TryParse(match.Value.Contains(".") ? match.Value : match.Value + ".0", out var version) ? version : new Version(0, 0);
    }

    private static DeviceUserGroup Group(Project project, string path) {
        DeviceUserGroup group = null;
        foreach (var name in path.Split('/')) {
            var groups = group is null ? project.DeviceGroups : group.Groups;
            group = groups.Find(name) ?? groups.Create(name);
        }
        return group;
    }

    private static Device FindDevice(Project project, string name) =>
        HardwareReport.Devices(project).Select(entry => entry.Device).FirstOrDefault(device => device.Name == name)
            ?? throw new InvalidOperationException($"No device {name}.");

    private static NetworkInterface Interface(Project project, string deviceName, string interfaceName) {
        var device = FindDevice(project, deviceName);
        var interfaces = All(device.DeviceItems).Where(item => item.GetService<NetworkInterface>() is not null).ToList();
        var found = interfaces.FirstOrDefault(item => item.Name == interfaceName)
            ?? throw new InvalidOperationException($"{deviceName} has no interface {interfaceName}; it has {string.Join(", ", interfaces.Select(item => item.Name))}.");
        return found.GetService<NetworkInterface>();
    }

    internal static IEnumerable<DeviceItem> All(IEnumerable<DeviceItem> items) {
        foreach (var item in items) {
            yield return item;
            foreach (var child in All(item.DeviceItems)) {
                yield return child;
            }
        }
    }
}

/// <summary>
/// Writes what TIA reports about a project's hardware through Openness (spec 0010, FR-013), for the library's tests to
/// compare with: every readable attribute of every device, device item, address, interface node, subnet and IO system, and
/// who is connected to whom. Rows are <c>kind, path, attribute, value</c>; a path joins names with " » ". No attribute named
/// like a password, key or certificate is written.
/// </summary>
internal static class HardwareReport {
    public const string Separator = " » ";
    private static readonly Regex Secret = new("password|passwd|secret|key|certificate|salt|hash", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Who changed the project and when: the user's name and the build's time, nothing the reader compares.</summary>
    private static readonly HashSet<string> Personal = new(StringComparer.Ordinal) { "Author", "InstallationDate" };

    public static IEnumerable<(string Group, Device Device)> Devices(Project project) {
        foreach (var device in project.Devices) {
            yield return ("", device);
        }
        foreach (var device in project.UngroupedDevicesGroup.Devices) {
            yield return ("", device);
        }
        foreach (var entry in Groups(project.DeviceGroups, "")) {
            yield return entry;
        }

        static IEnumerable<(string, Device)> Groups(DeviceUserGroupComposition groups, string path) {
            foreach (var group in groups) {
                var groupPath = path.Length == 0 ? group.Name : path + "/" + group.Name;
                foreach (var device in group.Devices) {
                    yield return (groupPath, device);
                }
                foreach (var entry in Groups(group.Groups, groupPath)) {
                    yield return entry;
                }
            }
        }
    }

    public static void Write(Project project, string path) {
        using var output = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        output.WriteLine("kind\tpath\tattribute\tvalue");
        var rows = 0;
        void Row(string kind, string at, string attribute, string value) {
            output.WriteLine(string.Join("\t", kind, Clean(at), Clean(attribute), Clean(value)));
            rows++;
        }
        void Attributes(string kind, string at, IEngineeringObject item) {
            foreach (var (name, value) in Readable(item)) {
                Row(kind, at, name, value);
            }
        }

        foreach (var (group, device) in Devices(project)) {
            Row("device", device.Name, "group", group);
            foreach (var identifier in device.HwIdentifiers) {
                Row("device", device.Name, "HwIdentifier", identifier.Identifier.ToString(CultureInfo.InvariantCulture));
            }
            Attributes("device", device.Name, device);
            foreach (var item in device.DeviceItems) {
                Item(device.Name, item);
            }
        }
        foreach (var subnet in project.Subnets) {
            Attributes("subnet", subnet.Name, subnet);
            foreach (var node in subnet.Nodes) {
                Row("subnet", subnet.Name, "node", NodePath(node));
            }
            foreach (var system in subnet.IoSystems) {
                Attributes("iosystem", system.Name, system);
                foreach (var identifier in system.HwIdentifiers) {
                    Row("iosystem", system.Name, "HwIdentifier", identifier.Identifier.ToString(CultureInfo.InvariantCulture));
                }
                Row("iosystem", system.Name, "subnet", subnet.Name);
                foreach (var connector in system.ConnectedIoDevices) {
                    Row("iosystem", system.Name, "iodevice", ItemPath(Owner(connector)));
                }
            }
        }
        Console.WriteLine($"hardware report: {rows} rows");

        void Item(string at, DeviceItem item) {
            var path = at + Separator + item.Name;
            Attributes("item", path, item);
            foreach (var identifier in item.HwIdentifiers) {
                Row("item", path, "HwIdentifier", identifier.Identifier.ToString(CultureInfo.InvariantCulture));
            }
            var index = 0;
            foreach (var address in item.Addresses) {
                Attributes("address", path + Separator + index.ToString(CultureInfo.InvariantCulture), address);
                index++;
            }
            if (item.GetService<NetworkInterface>() is { } networkInterface) {
                foreach (var node in networkInterface.Nodes) {
                    var nodePath = path + Separator + node.Name;
                    Attributes("node", nodePath, node);
                    Row("node", nodePath, "subnet", node.ConnectedSubnet?.Name ?? "");
                }
                foreach (var controller in networkInterface.IoControllers) {
                    Row("interface", path, "controls", controller.IoSystem?.Name ?? "");
                }
                foreach (var connector in networkInterface.IoConnectors) {
                    Row("interface", path, "connectedTo", connector.ConnectedToIoSystem?.Name ?? "");
                }
            }
            foreach (var child in item.DeviceItems) {
                Item(path, child);
            }
        }
    }

    private static string NodePath(Node node) => ItemPath(Owner(node)) + Separator + node.Name;

    /// <summary>The device item a node or IO connector belongs to: its parent is the interface's NetworkInterface service, whose parent is the item.</summary>
    private static DeviceItem Owner(IEngineeringObject item) {
        var parent = item.Parent;
        while (parent is not null and not DeviceItem) {
            parent = parent.Parent;
        }
        return (DeviceItem)parent;
    }

    private static string ItemPath(DeviceItem item) {
        var names = new List<string>();
        IEngineeringObject current = item;
        while (current is not null and not Project) {
            if (current is DeviceItem deviceItem) {
                names.Add(deviceItem.Name);
            } else if (current is Device device) {
                names.Add(device.Name);
                break;
            }
            current = current.Parent;
        }
        names.Reverse();
        return string.Join(Separator, names);
    }

    private static IEnumerable<(string Name, string Value)> Readable(IEngineeringObject item) {
        foreach (var info in item.GetAttributeInfos().OrderBy(info => info.Name, StringComparer.Ordinal)) {
            if (Secret.IsMatch(info.Name) || Personal.Contains(info.Name)) {
                continue;
            }
            object value;
            try {
                value = item.GetAttribute(info.Name);
            } catch (Exception e) when (e is EngineeringException or NotSupportedException or InvalidOperationException) {
                continue;
            }
            if (Format(value) is { } text) {
                yield return (info.Name, text);
            }
        }
    }

    private static string Format(object value) => value switch {
        null => null,
        string text => text,
        bool flag => flag ? "True" : "False",
        Enum => value.ToString(),
        IFormattable number when value.GetType().IsPrimitive => number.ToString(null, CultureInfo.InvariantCulture),
        DateTime time => time.ToString("o", CultureInfo.InvariantCulture),
        Version version => version.ToString(),
        IEnumerable list => list.Cast<object>().Select(Format).Where(text => text is not null).ToList() is { Count: > 0 } texts ? string.Join(",", texts) : null,
        _ => null,
    };

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
