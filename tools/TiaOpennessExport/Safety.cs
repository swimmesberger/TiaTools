using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Safety;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;

namespace TiaOpennessExport;

/// <summary>
/// Builds a reference project's safety program (spec 0012) from a PLC folder's <c>safety.tsv</c>: its F-runtime groups,
/// then the settings TIA's defaults would hide, applied before the PLC is compiled. A row TIA refuses is reported with TIA's
/// error and counted like a refused source. It never looks for, logs in with or enters a safety password; the only one it
/// knows is <see cref="ReferencePassword"/>, which it sets after compiling on a PLC whose folder holds <c>password.txt</c>.
/// </summary>
internal static class SafetyBuilder {
    /// <summary>The safety password of reference projects. Invented, it guards nothing, and the build never logs in with it.</summary>
    private const string ReferencePassword = "Reference-0012";

    public static int Configure(PlcSoftware plc, string sources) {
        var file = Path.Combine(sources, "safety.tsv");
        if (!File.Exists(file)) {
            return 0;
        }
        var administration = Cpu(plc).GetService<SafetyAdministration>()
            ?? throw new InvalidOperationException($"{plc.Name} has safety.tsv but no safety administration: is it an F-CPU?");
        var refused = 0;
        var number = 0;
        var defaultsRemoved = false;
        foreach (var line in File.ReadAllLines(file, Encoding.UTF8)) {
            number++;
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }
            var fields = line.Split('\t');
            try {
                switch (fields[0]) {
                    case "group": {
                        // A new F-CPU comes with a default F-runtime group and its blocks; a program that names its own drops them.
                        if (!defaultsRemoved) {
                            RemoveDefaultGroups(plc, administration);
                            defaultsRemoved = true;
                        }
                        var main = Program.FindBlock(plc.BlockGroup, fields[2]) ?? throw new InvalidOperationException($"No block {fields[2]}.");
                        var group = main.Parent as PlcBlockGroup ?? plc.BlockGroup;
                        var instance = group.Blocks.CreateInstanceDB(fields[3], true, 1, fields[2]);
                        administration.RuntimeGroups.Create(fields[1], main, instance);
                        break;
                    }
                    case "set":
                        Set(Target(plc, administration, fields[1]), fields[2], fields[3]);
                        break;
                    default:
                        throw new FormatException($"Unknown row {fields[0]}.");
                }
                Console.WriteLine($"safety {string.Join(" ", fields)}");
            } catch (Exception e) when (e is EngineeringException or InvalidOperationException or FormatException or IndexOutOfRangeException or InvalidCastException) {
                refused++;
                Console.WriteLine($"refused {plc.Name}/safety.tsv:{number}: {e.Message.Replace(Environment.NewLine, " ")}");
            }
        }
        return refused;
    }

    /// <summary>After compiling: sets the invented password on a PLC whose folder holds password.txt (spec 0012, FR-019).</summary>
    public static void Lock(PlcSoftware plc, string sources) {
        if (!File.Exists(Path.Combine(sources, "password.txt"))) {
            return;
        }
        using var password = new SecureString();
        foreach (var c in ReferencePassword) {
            password.AppendChar(c);
        }
        Cpu(plc).GetService<SafetyAdministration>().SetSafetyOfflineProgramPassword(password);
        Console.WriteLine($"safety password set on {plc.Name}");
    }

    internal static DeviceItem Cpu(PlcSoftware plc) {
        IEngineeringObject current = plc;
        while (current is not null and not DeviceItem) {
            current = current.Parent;
        }
        return (DeviceItem)current ?? throw new InvalidOperationException($"{plc.Name} is in no device item.");
    }

    private static void RemoveDefaultGroups(PlcSoftware plc, SafetyAdministration administration) {
        foreach (var group in administration.RuntimeGroups.ToList()) {
            var main = group.MainSafetyBlockName;
            var instance = group.MainSafetyBlockIDbName;
            group.Delete();
            Program.FindBlock(plc.BlockGroup, instance)?.Delete();
            Program.FindBlock(plc.BlockGroup, main)?.Delete();
        }
    }

    /// <summary>
    /// <c>group:&lt;name&gt;</c>, <c>settings</c>, <c>blocknumbers</c>, <c>cpu</c> (the PLC's own device item), or
    /// <c>item:&lt;device&gt;/&lt;item&gt;</c>, the device item of that name that has the attribute (a module's submodule for F-parameters).
    /// </summary>
    private static Func<string, IEngineeringObject> Target(PlcSoftware plc, SafetyAdministration administration, string name) {
        if (name.StartsWith("group:", StringComparison.Ordinal)) {
            var group = administration.RuntimeGroups.Find(name.Substring(6)) ?? throw new InvalidOperationException($"No F-runtime group {name.Substring(6)}.");
            return _ => group;
        }
        if (name.StartsWith("item:", StringComparison.Ordinal)) {
            var parts = name.Substring(5).Split('/');
            var device = HardwareReport.Devices(OwningProject(plc)).Select(entry => entry.Device).FirstOrDefault(candidate => candidate.Name == parts[0])
                ?? throw new InvalidOperationException($"No device {parts[0]}.");
            var items = HardwareBuilder.All(device.DeviceItems).Where(item => item.Name == parts[1]).ToList();
            return attribute => items.FirstOrDefault(item => item.GetAttributeInfos().Any(info => info.Name == attribute))
                ?? throw new InvalidOperationException($"{name} has no item with {attribute}.");
        }
        return name switch {
            "settings" => _ => administration.Settings,
            "blocknumbers" => _ => administration.Settings.AssignmentOfBlockNumbers,
            "cpu" => _ => Cpu(plc),
            _ => throw new FormatException($"Unknown object {name}."),
        };
    }

    private static Project OwningProject(PlcSoftware plc) {
        IEngineeringObject current = plc;
        while (current is not null and not Siemens.Engineering.Project) {
            current = current.Parent;
        }
        return (Siemens.Engineering.Project)current;
    }

    private static void Set(Func<string, IEngineeringObject> target, string attribute, string text) {
        var item = target(attribute);
        var current = item.GetAttribute(attribute);
        object value = current switch {
            Enum => Enum.Parse(current.GetType(), text),
            bool => bool.Parse(text),
            null => text,
            IConvertible => Convert.ChangeType(text, current.GetType(), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException($"{attribute} is a {current.GetType().Name}, which safety.tsv cannot set."),
        };
        item.SetAttribute(attribute, value);
    }
}

/// <summary>
/// Writes what TIA reports about each PLC's safety program through Openness (spec 0012, FR-015), for the library's tests:
/// <c>generated/safety/&lt;PLC&gt;.tsv</c>, rows <c>kind, name, attribute, value</c>, sorted. Values are as Openness gives
/// them. It reads no attribute named like a password, key or hash, and only asks whether a password is set.
/// </summary>
internal static class SafetyReport {
    private static readonly Regex Secret = new("password|passwd|secret|key|certificate|salt|hash", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Write(Project project, string folder) {
        foreach (var (_, device) in HardwareReport.Devices(project)) {
            foreach (var cpu in HardwareBuilder.All(device.DeviceItems)) {
                if (cpu.GetService<SoftwareContainer>()?.Software is not PlcSoftware plc || cpu.GetService<SafetyAdministration>() is not { } administration) {
                    continue;
                }
                var rows = new List<string>();
                void Row(string kind, string name, string attribute, object value) {
                    if (Format(value) is { } text) {
                        rows.Add(string.Join("\t", kind, Clean(name), attribute, Clean(text)));
                    }
                }
                void Attributes(string kind, string name, IEngineeringObject item, Func<string, bool> wanted) {
                    foreach (var info in item.GetAttributeInfos()) {
                        if (!wanted(info.Name) || Secret.IsMatch(info.Name)) {
                            continue;
                        }
                        try {
                            Row(kind, name, info.Name, item.GetAttribute(info.Name));
                        } catch (Exception e) when (e is EngineeringException or NotSupportedException or InvalidOperationException) {
                            // not readable here
                        }
                    }
                }

                Row("program", plc.Name, "PasswordSet", administration.IsSafetyOfflineProgramPasswordSet);
                Attributes("settings", plc.Name, administration.Settings, _ => true);
                Attributes("blocknumbers", plc.Name, administration.Settings.AssignmentOfBlockNumbers, _ => true);
                foreach (var signature in administration.ProgramSignatures.Signatures) {
                    Row("program", plc.Name, signature.Type.ToString(), signature.Value);
                }
                foreach (var group in administration.RuntimeGroups) {
                    Attributes("group", group.Name, group, _ => true);
                }
                Attributes("cpu", cpu.Name, cpu, name => name.StartsWith("Failsafe_", StringComparison.Ordinal));
                foreach (var (_, other) in HardwareReport.Devices(project)) {
                    foreach (var item in HardwareBuilder.All(other.DeviceItems)) {
                        var names = item.GetAttributeInfos().Select(info => info.Name).ToList();
                        if (!names.Contains("Failsafe_FDestinationAddress") || !names.Contains("Failsafe_FSourceAddress")) {
                            continue;
                        }
                        if (!Equals(item.GetAttribute("Failsafe_FSourceAddress"), cpu.GetAttribute("Failsafe_CentralFSourceAddress"))) {
                            continue;
                        }
                        var name = other.Name + "/" + item.Name;
                        Attributes("fio", name, item, attribute => attribute.StartsWith("Failsafe_F", StringComparison.Ordinal) || attribute.StartsWith("Failsafe_Manual", StringComparison.Ordinal));
                        foreach (var address in item.Addresses) {
                            Row("fio", name, "StartAddress." + address.IoType, address.StartAddress);
                        }
                    }
                }
                Blocks(plc.BlockGroup, "");

                void Blocks(PlcBlockGroup group, string path) {
                    foreach (var block in group.Blocks) {
                        if (block.GetService<SafetySignatureProvider>() is not { } provider) {
                            continue;
                        }
                        Row("block", block.Name, "Kind", block.GetType().Name);
                        Row("block", block.Name, "Number", block.Number);
                        Row("block", block.Name, "ProgrammingLanguage", block.ProgrammingLanguage);
                        Row("block", block.Name, "Folder", path);
                        foreach (var signature in provider.Signatures) {
                            Row("block", block.Name, signature.Type.ToString(), signature.Value);
                        }
                    }
                    foreach (var child in group.Groups) {
                        Blocks(child, path.Length == 0 ? child.Name : path + "/" + child.Name);
                    }
                    if (group is PlcBlockSystemGroup system) {
                        foreach (var child in system.SystemBlockGroups) {
                            SystemBlocks(child, child.Name);
                        }
                    }
                }

                void SystemBlocks(PlcSystemBlockGroup group, string path) {
                    foreach (var block in group.Blocks) {
                        if (block.GetService<SafetySignatureProvider>() is not { } provider) {
                            continue;
                        }
                        Row("block", block.Name, "Kind", block.GetType().Name);
                        Row("block", block.Name, "Number", block.Number);
                        Row("block", block.Name, "ProgrammingLanguage", block.ProgrammingLanguage);
                        Row("block", block.Name, "Folder", path);
                        foreach (var signature in provider.Signatures) {
                            Row("block", block.Name, signature.Type.ToString(), signature.Value);
                        }
                    }
                    foreach (var child in group.Groups) {
                        SystemBlocks(child, path + "/" + child.Name);
                    }
                }

                Directory.CreateDirectory(folder);
                rows.Sort(StringComparer.Ordinal);
                File.WriteAllText(Path.Combine(folder, plc.Name + ".tsv"), "kind\tname\tattribute\tvalue\n" + string.Join("\n", rows) + "\n", new UTF8Encoding(false));
                Console.WriteLine($"safety report {plc.Name}: {rows.Count} rows");
            }
        }
    }

    private static string Format(object value) => value switch {
        null => null,
        string text => text,
        bool flag => flag ? "True" : "False",
        Enum => value.ToString(),
        SafetySystemVersion version => version.Value,
        IFormattable number when value.GetType().IsPrimitive => number.ToString(null, CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
