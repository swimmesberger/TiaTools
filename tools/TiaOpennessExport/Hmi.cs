using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.CommunicationConnections;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace TiaOpennessExport;

/// <summary>
/// HMI devices of a reference project (spec 0011). A sources folder with a device.txt is an HMI device: Unified, built
/// through the HmiUnified API from tags.tsv, alarms.tsv and screens.tsv, or Advanced, a WinCC RT Advanced runtime in an
/// ET 200SP Open Controller, whose software PLC is built from a PLC folder inside it and whose tag tables and screens are
/// SimaticML imported from "HMI tags" and "Screens". Each gets an integrated HMI connection to its PLC through the hardware
/// API. After the build, what TIA says about each HMI device is written to generated/&lt;HMI device&gt;/.
/// </summary>
internal static class Hmi {
    public static bool IsHmiFolder(string folder) => File.Exists(Path.Combine(folder, "device.txt"));

    /// <summary>Builds the HMI device of one sources folder; returns how many of its parts TIA refused.</summary>
    public static int Build(Project project, string folder, Func<PlcSoftware, string, int> buildPlc) {
        var name = Path.GetFileName(folder);
        var settings = File.ReadAllLines(Path.Combine(folder, "device.txt"), Encoding.UTF8)
            .Where(line => line.Length != 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Select(line => line.Split('\t')).ToDictionary(fields => fields[0], fields => fields[1], StringComparer.Ordinal);
        try {
            return settings["family"] switch {
                "Unified" => BuildUnified(project, folder, name, settings),
                "Advanced" => BuildAdvanced(project, folder, name, settings, buildPlc),
                var other => throw new InvalidOperationException($"{name}: family {other} is neither Unified nor Advanced"),
            };
        } catch (EngineeringException e) {
            Console.WriteLine($"refused {name}: {e.Message.Replace(Environment.NewLine, " ")}");
            return 1;
        }
    }

    private static int BuildUnified(Project project, string folder, string name, Dictionary<string, string> settings) {
        var device = project.Devices.CreateWithItem(settings["type"], name, name);
        var runtime = Items(device).Single(item => item.GetService<SoftwareContainer>()?.Software is HmiSoftware);
        runtime.Name = name;
        var hmi = (HmiSoftware)runtime.GetService<SoftwareContainer>().Software;
        var local = Items(device).Select(item => item.GetService<NetworkInterface>())
            .First(network => network is not null && network.InterfaceType == NetType.Ethernet && network.Nodes.Count != 0).Nodes[0];
        var (cpu, partner) = PlcNode(project, settings["plc"]);
        local.ConnectToSubnet(partner.ConnectedSubnet ?? partner.CreateAndConnectToSubnet("PN/IE_1"));
        runtime.GetService<CommunicationManagement>().Connections.Create<HmiConnection>(local, cpu, partner);
        var connection = hmi.Connections.Single().Name;
        Console.WriteLine($"{name}: connection {connection} to {settings["plc"]}");
        project.Save();

        var refused = 0;
        foreach (var row in Rows(Path.Combine(folder, "tags.tsv"))) {
            refused += Attempt(project, $"{name} tag {row[1]}", () => {
                if (hmi.TagTables.Find(row[0]) is null) {
                    hmi.TagTables.Create(row[0]);
                }
                var tag = hmi.Tags.Create(row[1], row[0]);
                if (row[2].Length != 0) {
                    tag.Connection = connection;
                    tag.PlcTag = row[2];
                } else {
                    Set(tag, "DataType", row[3]);
                }
            });
        }
        foreach (var row in Rows(Path.Combine(folder, "alarms.tsv"))) {
            // kind, name, trigger tag, bit or condition, limit, class, text
            refused += Attempt(project, $"{name} alarm {row[1]}", () => {
                object alarm = row[0] == "discrete" ? hmi.DiscreteAlarms.Create(row[1]) : hmi.AnalogAlarms.Create(row[1]);
                Set(alarm, "RaisedStateTag", row[2]);
                if (row[0] == "discrete") {
                    Set(alarm, "RaisedStateTagBitNumber", row[3]);
                } else {
                    Set(alarm, "Condition", row[3]);
                    Set(alarm, "ConditionValue", row[4]);
                }
                Set(alarm, "AlarmClass", row[5]);
                SetText(alarm, "EventText", row[6]);
            });
        }
        foreach (var row in Rows(Path.Combine(folder, "screens.tsv"))) {
            // screen, number, item, type, left, top, width, height, bound tag or text
            refused += Attempt(project, $"{name} screen {row[0]} {row[2]}", () => {
                var screen = hmi.Screens.Find(row[0]);
                if (screen is null) {
                    screen = hmi.Screens.Create(row[0]);
                    Set(screen, "ScreenNumber", row[1]);
                }
                HmiScreenItemBase item = row[3] switch {
                    "IOField" => screen.ScreenItems.Create<HmiIOField>(row[2]),
                    "Text" => screen.ScreenItems.Create<HmiText>(row[2]),
                    "Button" => screen.ScreenItems.Create<HmiButton>(row[2]),
                    var other => throw new InvalidOperationException($"screen item type {other}"),
                };
                Set(item, "Left", row[4]);
                Set(item, "Top", row[5]);
                Set(item, "Width", row[6]);
                Set(item, "Height", row[7]);
                if (row[3] == "IOField" && row[8].Length != 0) {
                    item.Dynamizations.Create<TagDynamization>("ProcessValue").Tag = row[8];
                } else if (row[3] == "Text" && row[8].Length != 0) {
                    SetText(item, "Text", row[8]);
                }
            });
        }
        return refused;
    }

    private static int BuildAdvanced(Project project, string folder, string name, Dictionary<string, string> settings, Func<PlcSoftware, string, int> buildPlc) {
        var device = project.Devices.CreateWithItem(settings["type"], settings["device"], settings["device"]);
        var runtime = Items(device).Single(item => item.GetService<SoftwareContainer>()?.Software is HmiTarget);
        var controller = Items(device).Single(item => item.GetService<SoftwareContainer>()?.Software is PlcSoftware);
        // In an Open Controller TIA may keep its own names for the runtime and the software PLC; the generated files say which.
        Rename(runtime, ((IEngineeringObject)runtime.GetService<SoftwareContainer>().Software), name);
        Rename(controller, ((IEngineeringObject)controller.GetService<SoftwareContainer>().Software), settings["plc"]);
        var refused = buildPlc((PlcSoftware)controller.GetService<SoftwareContainer>().Software, Path.Combine(folder, settings["plc"]));
        // The runtime reaches its own software PLC over the PC-internal subnet; to a PLC outside the station TIA makes the
        // connection, but no HMI tag can be linked over it (plan 0011).
        var local = Items(device).Single(item => item.Name == "Computer interface").GetService<NetworkInterface>().Nodes[0];
        var partner = Items(device).Single(item => item.Name == "PC communications interface").GetService<NetworkInterface>().Nodes[0];
        var connection = runtime.GetService<CommunicationManagement>().Connections.Create<HmiConnection>(local, controller, partner).LocalConnectionName;
        Console.WriteLine($"{name}: connection {connection} to {controller.Name}");
        project.Save();
        // The sources name the connection @Connection@: TIA numbers connections across the project, and an import that
        // names one that does not exist ends the Openness session.
        foreach (var file in Files(Path.Combine(folder, "HMI tags"))) {
            var resolved = Path.Combine(Path.GetTempPath(), "TiaOpennessExport-" + Path.GetFileName(file));
            File.WriteAllText(resolved, File.ReadAllText(file, Encoding.UTF8).Replace("@Connection@", connection), new UTF8Encoding(false));
            refused += Attempt(project, $"{name} tag table {Path.GetFileName(file)}", () => Target(device).TagFolder.TagTables.Import(new FileInfo(resolved), ImportOptions.None));
            File.Delete(resolved);
        }
        foreach (var file in Files(Path.Combine(folder, "Screens"))) {
            refused += Attempt(project, $"{name} screen {Path.GetFileName(file)}", () => Target(device).ScreenFolder.Screens.Import(new FileInfo(file), ImportOptions.None));
        }
        return refused;
    }

    /// <summary>Writes generated/&lt;HMI device&gt;/ for every HMI device of the project: what TIA says about it.</summary>
    public static void Export(Project project, string generated) {
        foreach (var device in AllDevices(project)) {
            foreach (var item in Items(device)) {
                switch (item.GetService<SoftwareContainer>()?.Software) {
                    case HmiSoftware unified:
                        ExportUnified(item, unified, Path.Combine(generated, item.Name));
                        break;
                    case HmiTarget advanced:
                        ExportAdvanced(item, advanced, Path.Combine(generated, item.Name));
                        break;
                }
            }
        }
    }

    private static void ExportUnified(DeviceItem runtime, HmiSoftware hmi, string folder) {
        Directory.CreateDirectory(folder);
        Write(folder, "device.tsv", ["name", "family", "type"], [[runtime.Name, "Unified", runtime.TypeIdentifier]]);
        Write(folder, "connections.tsv", ["name", "partner", "driver"],
            hmi.Connections.Select(connection => new[] { connection.Name, Text(connection, "Partner"), Text(connection, "CommunicationDriver") }));
        Write(folder, "tags.tsv", ["table", "name", "connection", "plc tag", "data type", "acquisition cycle"],
            hmi.Tags.Select(tag => new[] { tag.TagTableName, tag.Name, Text(tag, "Connection"), Text(tag, "PlcTag"), Text(tag, "DataType"), Text(tag, "AcquisitionCycle") }));
        var alarms = new List<string[]>();
        alarms.AddRange(hmi.DiscreteAlarms.Select(alarm => new[] { "discrete", alarm.Name, Text(alarm, "Id"), Text(alarm, "AlarmClass"), Text(alarm, "RaisedStateTag"), Text(alarm, "RaisedStateTagBitNumber"), "", RichText(alarm, "EventText") }));
        alarms.AddRange(hmi.AnalogAlarms.Select(alarm => new[] { "analog", alarm.Name, Text(alarm, "Id"), Text(alarm, "AlarmClass"), Text(alarm, "RaisedStateTag"), Text(alarm, "Condition"), Text(alarm, "ConditionValue"), RichText(alarm, "EventText") }));
        Write(folder, "alarms.tsv", ["kind", "name", "number", "class", "trigger tag", "bit or limit mode", "limit", "text"], alarms);
        var screens = new List<string[]>();
        foreach (var screen in hmi.Screens) {
            foreach (var item in screen.ScreenItems) {
                var tag = item.Dynamizations.OfType<TagDynamization>().Select(dynamization => dynamization.Tag).FirstOrDefault() ?? "";
                screens.Add([screen.Name, Text(screen, "ScreenNumber"), Text(screen, "Width"), Text(screen, "Height"),
                    item.Name, item.GetType().Name, Text(item, "Left"), Text(item, "Top"), Text(item, "Width"), Text(item, "Height"), tag]);
            }
        }
        Write(folder, "screens.tsv", ["screen", "number", "width", "height", "item", "type", "left", "top", "item width", "item height", "tag"], screens);
    }

    private static void ExportAdvanced(DeviceItem runtime, HmiTarget hmi, string folder) {
        Directory.CreateDirectory(folder);
        Write(folder, "device.tsv", ["name", "family", "type"], [[runtime.Name, "Comfort/Advanced", runtime.TypeIdentifier]]);
        Write(folder, "connections.tsv", ["name", "partner", "driver"],
            runtime.GetService<CommunicationManagement>().Connections.OfType<HmiConnection>()
                .Select(connection => new[] { connection.LocalConnectionName, connection.PartnerTarget?.Name ?? "", "" }));
        foreach (var table in hmi.TagFolder.TagTables) {
            table.Export(new FileInfo(Path.Combine(folder, "TAGS." + table.Name + ".xml")), ExportOptions.WithDefaults);
        }
        foreach (var screen in hmi.ScreenFolder.Screens) {
            screen.Export(new FileInfo(Path.Combine(folder, "SCREEN." + screen.Name + ".xml")), ExportOptions.WithDefaults);
        }
    }

    /// <summary>Names a device item, or failing that its software; leaves TIA's name where neither takes one.</summary>
    private static void Rename(DeviceItem item, IEngineeringObject software, string name) {
        foreach (var target in new IEngineeringObject[] { item, software }) {
            try {
                target.SetAttribute("Name", name);
                Console.WriteLine($"named {name}");
                return;
            } catch (EngineeringException) {
                // Read-only here: try the next one.
            }
        }
        Console.WriteLine($"kept TIA's name {item.Name} instead of {name}");
    }

    private static (DeviceItem Cpu, Node Node) PlcNode(Project project, string plc) {
        var device = project.Devices.Find(plc) ?? throw new InvalidOperationException($"no PLC device {plc} for the HMI connection");
        var cpu = Items(device).Single(item => item.GetService<SoftwareContainer>()?.Software is PlcSoftware);
        var node = Items(device).Select(item => item.GetService<NetworkInterface>())
            .First(network => network is not null && network.InterfaceType == NetType.Ethernet && network.Nodes.Count != 0).Nodes[0];
        return (cpu, node);
    }

    private static HmiTarget Target(Device device) =>
        (HmiTarget)Items(device).Single(item => item.GetService<SoftwareContainer>()?.Software is HmiTarget).GetService<SoftwareContainer>().Software;

    private static IEnumerable<Device> AllDevices(Project project) {
        var devices = new List<Device>(project.Devices);
        devices.AddRange(project.UngroupedDevicesGroup.Devices);
        return devices.Distinct();
    }

    private static IEnumerable<DeviceItem> Items(Device device) => device.DeviceItems.SelectMany(Items);

    private static IEnumerable<DeviceItem> Items(DeviceItem item) => new[] { item }.Concat(item.DeviceItems.SelectMany(Items));

    /// <summary>Saves before each part: an import TIA refuses while creating an object can end the Openness session.</summary>
    private static int Attempt(Project project, string what, Action action) {
        project.Save();
        try {
            action();
            Console.WriteLine($"built {what}");
            return 0;
        } catch (Exception e) when (e is EngineeringException or System.Reflection.TargetInvocationException or InvalidOperationException or FormatException or ArgumentException) {
            var cause = e is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : e;
            Console.WriteLine($"refused {what}: {cause.Message.Replace(Environment.NewLine, " ")}");
            return 1;
        }
    }

    private static IEnumerable<string[]> Rows(string file) =>
        File.Exists(file)
            ? File.ReadAllLines(file, Encoding.UTF8).Skip(1).Where(line => line.Length != 0 && !line.StartsWith("#", StringComparison.Ordinal)).Select(line => line.Split('\t'))
            : Enumerable.Empty<string[]>();

    private static IEnumerable<string> Files(string folder) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "*.xml").OrderBy(file => file, StringComparer.Ordinal) : Enumerable.Empty<string>();

    /// <summary>Sets a property by name, converting the text to its type, so a source row needs no type of its own.</summary>
    private static void Set(object target, string property, string value) {
        var info = target.GetType().GetProperty(property) ?? throw new InvalidOperationException($"{target.GetType().Name} has no {property}");
        var type = Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType;
        // A property typed object, such as an analog alarm's ConditionValue, takes a number as a double.
        object converted = type.IsEnum ? Enum.Parse(type, value)
            : type == typeof(string) ? value
            : type == typeof(object) ? double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : value
            : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        info.SetValue(target, converted, null);
    }

    /// <summary>Sets the first item of a multilingual text; TIA takes HMI texts as rich text.</summary>
    private static void SetText(object target, string property, string value) =>
        ((MultilingualText)target.GetType().GetProperty(property).GetValue(target, null)).Items.First().Text = "<body><p>" + value + "</p></body>";

    private static string Text(object target, string property) {
        var value = target.GetType().GetProperty(property)?.GetValue(target, null);
        return value switch {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static string RichText(object target, string property) =>
        ((MultilingualText)target.GetType().GetProperty(property).GetValue(target, null)).Items.First().Text ?? "";

    private static void Write(string folder, string file, string[] header, IEnumerable<string[]> rows) {
        var text = new StringBuilder();
        text.Append(string.Join("\t", header)).Append('\n');
        foreach (var row in rows) {
            text.Append(string.Join("\t", row.Select(field => (field ?? "").Replace('\t', ' ').Replace("\r", "").Replace('\n', ' ')))).Append('\n');
        }
        File.WriteAllText(Path.Combine(folder, file), text.ToString(), new UTF8Encoding(false));
    }
}
