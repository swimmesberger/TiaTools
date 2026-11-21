using System.Globalization;
using System.Text.Json;

namespace TiaFormat.Hardware;

/// <summary>Writes a <see cref="TiaHardwareReport"/> as text for people, or as JSON for programs, with the same content.</summary>
public static class TiaHardwareWriter {
    /// <summary>
    /// The text <c>tiadump devices</c> writes: each device with its device items indented below it, by slot, then the
    /// subnets with their interfaces, then the IO systems with their IO controller and IO devices.
    /// </summary>
    public static void WriteText(TiaHardwareReport report, TextWriter output) {
        if (report.Devices.Count == 0 && report.Subnets.Count == 0) {
            output.WriteLine("The project has no devices.");
            return;
        }
        foreach (var device in report.Devices) {
            output.Write("Device ");
            output.Write(device.Name);
            Parts(output, device.TypeName is null ? null : $"({device.TypeName})", device.Group.Length == 0 ? null : $"in {device.Group}", Constants(device.Constants));
            output.WriteLine();
            foreach (var item in device.Items) {
                WriteItem(item, 1, output);
            }
            output.WriteLine();
        }
        foreach (var subnet in report.Subnets) {
            output.Write("Subnet ");
            output.Write(subnet.Name);
            Parts(output, $"({Kind(subnet.Kind)})");
            output.WriteLine();
            foreach (var path in subnet.Interfaces) {
                output.Write("  ");
                output.WriteLine(path);
            }
            output.WriteLine();
        }
        foreach (var system in report.IoSystems) {
            output.Write("IO system ");
            output.Write(system.Name);
            Parts(output,
                system.Number is { } number ? string.Create(CultureInfo.InvariantCulture, $"({number})") : null,
                system.Subnet is null ? null : $"on {system.Subnet}",
                Constants(system.Constants));
            output.WriteLine();
            output.Write("  IO controller ");
            output.WriteLine(system.Controller ?? "none");
            foreach (var path in system.IoDevices) {
                output.Write("  IO device ");
                output.WriteLine(path);
            }
            output.WriteLine();
        }
    }

    private static void WriteItem(TiaDeviceItem item, int depth, TextWriter output) {
        output.Write(new string(' ', depth * 2));
        output.Write(item.Slot?.ToString(CultureInfo.InvariantCulture) ?? "-");
        output.Write(' ');
        output.Write(item.Name);
        Parts(output,
            item.TypeName ?? item.Label,
            item.OrderNumber,
            item.Firmware,
            item.IoAddresses.Count == 0 ? null : string.Join(", ", item.IoAddresses),
            Constants(item.Constants));
        output.WriteLine();
        foreach (var address in item.InterfaceAddresses) {
            output.Write(new string(' ', (depth + 1) * 2));
            output.WriteLine(Describe(address));
        }
        foreach (var child in item.Items) {
            WriteItem(child, depth + 1, output);
        }
    }

    /// <summary>One interface's addresses in a line: <c>X1 on PN/IE_1: IP 192.168.0.1, mask 255.255.255.0, device name controller</c>.</summary>
    public static string Describe(TiaInterfaceAddress address) {
        var parts = new List<string>();
        if (address.Kind == TiaSubnetKind.Profibus) {
            parts.Add(address.ProfibusAddress is { } number ? string.Create(CultureInfo.InvariantCulture, $"PROFIBUS address {number}") : "no PROFIBUS address");
        } else {
            if (address.IpAddressSetElsewhere) {
                parts.Add("IP address set at the device");
            } else if (address.IpAddress is not null) {
                parts.Add($"IP {address.IpAddress}");
                if (address.SubnetMask is not null) {
                    parts.Add($"mask {address.SubnetMask}");
                }
            }
            if (address.DeviceName is not null) {
                parts.Add($"device name {address.DeviceName}{(address.DeviceNameGenerated == true ? " (generated)" : "")}");
            }
        }
        return $"{address.Name} on {address.Subnet ?? "no subnet"}: {(parts.Count == 0 ? "no address" : string.Join(", ", parts))}";
    }

    private static string? Constants(IReadOnlyList<TiaSystemConstant> constants) =>
        constants.Count == 0 ? null : string.Join(", ", constants.Select(constant => constant.HardwareIdentifier is { } id
            ? string.Create(CultureInfo.InvariantCulture, $"{constant.Name} = {id}")
            : constant.Name));

    private static void Parts(TextWriter output, params string?[] parts) {
        foreach (var part in parts) {
            if (!string.IsNullOrEmpty(part)) {
                output.Write("  ");
                output.Write(part);
            }
        }
    }

    private static string Kind(TiaSubnetKind kind) => kind switch {
        TiaSubnetKind.Ethernet => "PROFINET/Industrial Ethernet",
        TiaSubnetKind.Profibus => "PROFIBUS",
        _ => "other",
    };

    /// <summary>The report as JSON: <c>devices</c>, <c>subnets</c> and <c>ioSystems</c>.</summary>
    public static void WriteJson(TiaHardwareReport report, Stream output) {
        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteStartArray("devices");
        foreach (var device in report.Devices) {
            json.WriteStartObject();
            json.WriteString("name", device.Name);
            json.WriteString("group", device.Group);
            json.WriteString("type", device.TypeName);
            WriteConstants(json, device.Constants);
            WriteItems(json, device.Items);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartArray("subnets");
        foreach (var subnet in report.Subnets) {
            json.WriteStartObject();
            json.WriteString("name", subnet.Name);
            json.WriteString("kind", subnet.Kind.ToString());
            Strings(json, "interfaces", subnet.Interfaces);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartArray("ioSystems");
        foreach (var system in report.IoSystems) {
            json.WriteStartObject();
            json.WriteString("name", system.Name);
            Number(json, "number", system.Number);
            json.WriteString("kind", system.Kind.ToString());
            json.WriteString("subnet", system.Subnet);
            json.WriteString("ioController", system.Controller);
            Strings(json, "ioDevices", system.IoDevices);
            WriteConstants(json, system.Constants);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteItems(Utf8JsonWriter json, IReadOnlyList<TiaDeviceItem> items) {
        json.WriteStartArray("items");
        foreach (var item in items) {
            json.WriteStartObject();
            json.WriteString("name", item.Name);
            json.WriteString("path", item.Path);
            Number(json, "slot", item.Slot);
            json.WriteString("orderNumber", item.OrderNumber);
            json.WriteString("firmware", item.Firmware);
            json.WriteString("type", item.TypeName);
            json.WriteString("label", item.Label);
            WriteConstants(json, item.Constants);
            json.WriteStartArray("ioAddresses");
            foreach (var address in item.IoAddresses) {
                json.WriteStartObject();
                json.WriteString("kind", address.Kind.ToString());
                json.WriteNumber("startByte", address.StartByte);
                json.WriteNumber("lastByte", address.LastByte);
                json.WriteNumber("lengthInBits", address.LengthInBits);
                json.WriteString("text", address.ToString());
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("interfaceAddresses");
            foreach (var address in item.InterfaceAddresses) {
                json.WriteStartObject();
                json.WriteString("name", address.Name);
                json.WriteString("kind", address.Kind.ToString());
                json.WriteString("subnet", address.Subnet);
                json.WriteString("ipAddress", address.IpAddress);
                json.WriteString("subnetMask", address.SubnetMask);
                json.WriteBoolean("ipAddressSetAtDevice", address.IpAddressSetElsewhere);
                json.WriteString("deviceName", address.DeviceName);
                if (address.DeviceNameGenerated is { } generated) {
                    json.WriteBoolean("deviceNameGenerated", generated);
                } else {
                    json.WriteNull("deviceNameGenerated");
                }
                Number(json, "profibusAddress", address.ProfibusAddress);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            WriteItems(json, item.Items);
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    private static void WriteConstants(Utf8JsonWriter json, IReadOnlyList<TiaSystemConstant> constants) {
        json.WriteStartArray("systemConstants");
        foreach (var constant in constants) {
            json.WriteStartObject();
            json.WriteString("name", constant.Name);
            Number(json, "hardwareIdentifier", constant.HardwareIdentifier);
            json.WriteString("type", constant.Type);
            json.WriteString("plc", constant.Plc);
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    private static void Strings(Utf8JsonWriter json, string name, IReadOnlyList<string> values) {
        json.WriteStartArray(name);
        foreach (var value in values) {
            json.WriteStringValue(value);
        }
        json.WriteEndArray();
    }

    private static void Number(Utf8JsonWriter json, string name, long? value) {
        if (value is { } number) {
            json.WriteNumber(name, number);
        } else {
            json.WriteNull(name);
        }
    }
}
