using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TiaFormat.Program;

/// <summary>Writes a <see cref="TiaHmiReport"/> as Markdown for people, as JSON for programs, and as a dump's text files.</summary>
public static class TiaHmiWriter {
    /// <summary>The Markdown report: per HMI device its connections, tag tables, alarms and screens.</summary>
    public static void WriteMarkdown(TiaHmiReport report, string title, TextWriter output) {
        output.Write("# HMI: ");
        output.WriteLine(title);
        if (report.Devices.Count == 0) {
            output.WriteLine();
            output.WriteLine("The project has no HMI device.");
        }
        foreach (var device in report.Devices) {
            output.WriteLine();
            output.WriteLine($"## HMI device {device.Name}");
            output.WriteLine();
            output.WriteLine($"{device.FamilyName}{(device.DeviceType is { Length: > 0 } type ? ", " + type : "")}.");
            WriteConnections(device, output);
            WriteTags(device, output);
            WriteAlarms(device, output);
            WriteScreens(device, output);
            if (device.Code is { } code) {
                WriteCode(code, output);
            }
        }
    }

    /// <summary>The device's code (spec 0017): its HMI scripts, global script modules, VB scripts and custom web controls.</summary>
    private static void WriteCode(TiaHmiCode code, TextWriter output) {
        output.WriteLine();
        output.WriteLine("### Scripts");
        if (code.Scripts.Count == 0) {
            output.WriteLine();
            output.WriteLine("None.");
        }
        foreach (var script in code.Scripts) {
            output.WriteLine();
            output.WriteLine($"#### {Attached(script)}");
            Fence(script.Code, "javascript", output);
        }
        if (code.Modules.Count != 0) {
            output.WriteLine();
            output.WriteLine("### Global script modules");
            foreach (var module in code.Modules) {
                output.WriteLine();
                output.WriteLine($"#### {module.Name}");
                Fence(module.Code, "javascript", output);
            }
        }
        if (code.VbScripts.Count != 0) {
            output.WriteLine();
            output.WriteLine("### VB scripts");
            foreach (var script in code.VbScripts) {
                output.WriteLine();
                output.WriteLine($"#### {Path(script.Folder ?? "", script.Name)}");
                Fence(VbSource(script), "vb", output);
            }
        }
        if (code.CustomWebControls.Count != 0) {
            output.WriteLine();
            output.WriteLine("### Custom web controls");
            foreach (var control in code.CustomWebControls) {
                output.WriteLine();
                output.WriteLine($"#### {control.DisplayName ?? control.Guid}");
                output.WriteLine();
                output.WriteLine($"GUID {control.Guid}; used by {(control.UsedBy.Count == 0 ? "no screen item" : string.Join(", ", control.UsedBy.Select(use => use.Screen + "/" + use.Item)))}.");
                if (!control.HasPackage) {
                    output.WriteLine();
                    output.WriteLine($"The archive does not hold its package, {control.PackagePath}.");
                }
                foreach (var file in control.Files) {
                    output.WriteLine();
                    output.WriteLine($"##### {file.Path}");
                    if (file.Text is { } text) {
                        Fence(text, FenceLanguage(file.Path), output);
                    } else {
                        output.WriteLine();
                        output.WriteLine(Invariant($"{file.Content.Length} bytes."));
                    }
                }
            }
        }
    }

    /// <summary>A script's name in a tree of its screen's scripts: <c>Item.Tapped</c>, or <c>Screen.Loaded</c> for the screen's own.</summary>
    public static string EntryName(TiaHmiScript script) => (script.Item ?? script.Screen) + "." + script.Name;

    /// <summary>Where a script is attached, as <c>Screen/Item event Tapped</c> or <c>Screen property BackColor</c>.</summary>
    public static string Attached(TiaHmiScript script) =>
        $"{script.Screen}{(script.Item is null ? "" : "/" + script.Item)} {(script.Kind == TiaHmiScriptKind.Event ? "event" : "property")} {script.Name}";

    /// <summary>A VB script as source: its <c>Sub</c> or <c>Function</c> line with parameters, its code, and its end line.</summary>
    public static string VbSource(TiaHmiVbScript script) {
        var kind = script.ReturnType is null or "Sub" ? "Sub" : "Function";
        var text = new StringBuilder();
        text.Append(kind).Append(' ').Append(script.Name).Append('(')
            .AppendJoin(", ", script.Parameters.Select(parameter => (parameter.Declaration.Length == 0 ? "" : parameter.Declaration + " ") + parameter.Name)).Append(")\n");
        if (script.PreCode is { Length: > 0 } pre) {
            text.Append(pre.TrimEnd('\n')).Append('\n');
        }
        if (script.Code.Length != 0) {
            text.Append(script.Code.TrimEnd('\n')).Append('\n');
        }
        return text.Append("End ").Append(kind).Append('\n').ToString();
    }

    private static string FenceLanguage(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch {
        ".js" or ".mjs" => "javascript",
        ".htm" or ".html" => "html",
        ".css" => "css",
        ".json" => "json",
        ".svg" or ".xml" => "xml",
        _ => "",
    };

    private static void Fence(string code, string language, TextWriter output) {
        var fence = "```";
        while (code.Contains(fence, StringComparison.Ordinal)) {
            fence += "`";
        }
        output.WriteLine();
        output.WriteLine(fence + language);
        output.WriteLine(code.TrimEnd('\n'));
        output.WriteLine(fence);
    }

    private static void WriteConnections(TiaHmiDevice device, TextWriter output) {
        output.WriteLine();
        output.WriteLine("### Connections");
        output.WriteLine();
        if (device.Connections.Count == 0) {
            output.WriteLine("None.");
            return;
        }
        output.WriteLine("| Connection | PLC | Driver |");
        output.WriteLine("|---|---|---|");
        foreach (var connection in device.Connections) {
            output.WriteLine($"| {Cell(connection.Name)} | {Cell(connection.Plc ?? "")} | {Cell(connection.DriverName ?? "")} |");
        }
    }

    private static void WriteTags(TiaHmiDevice device, TextWriter output) {
        output.WriteLine();
        output.WriteLine("### Tag tables");
        if (device.TagTables.Count == 0) {
            output.WriteLine();
            output.WriteLine("None.");
        }
        foreach (var table in device.TagTables.Where(table => table.Tags.Count != 0)) {
            output.WriteLine();
            output.WriteLine($"#### {Path(table.Folder, table.Name)}");
            output.WriteLine();
            output.WriteLine("| HMI tag | Type | Connection | Cycle | Reads |");
            output.WriteLine("|---|---|---|---|---|");
            foreach (var tag in table.Tags) {
                output.WriteLine($"| {Cell(tag.Name)} | {Cell(tag.DataType ?? "")} | {Cell(tag.Connection ?? "internal")} | {Cell(tag.AcquisitionCycle ?? "")} | {Cell(Reads(tag))} |");
            }
        }
    }

    /// <summary>What an HMI tag reads: its PLC tag or data block member, its address, or nothing for an internal tag.</summary>
    public static string Reads(TiaHmiTag tag) => tag.PlcLink is { } link ? link.Display : tag.Address ?? "";

    private static void WriteAlarms(TiaHmiDevice device, TextWriter output) {
        output.WriteLine();
        output.WriteLine("### Alarms");
        output.WriteLine();
        if (device.Alarms.Count == 0) {
            output.WriteLine("None.");
            return;
        }
        output.WriteLine("| Kind | Number | Alarm | Class | Trigger tag | Raised by | Text |");
        output.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var alarm in device.Alarms) {
            output.WriteLine($"| {Kind(alarm)} | {Number(alarm.Number)} | {Cell(alarm.Name)} | {Cell(alarm.AlarmClass ?? "")} | {Cell(alarm.TriggerTag ?? "")} | {Cell(RaisedBy(alarm))} | {Cell(alarm.Text ?? "")} |");
        }
    }

    /// <summary>What raises an alarm: a discrete alarm's bit, or an analog alarm's limit mode and limit.</summary>
    public static string RaisedBy(TiaHmiAlarm alarm) =>
        alarm.Kind == TiaHmiAlarmKind.Discrete
            ? alarm.Bit is { } bit ? "bit " + bit.ToString(CultureInfo.InvariantCulture) : ""
            : string.Join(" ", new[] { alarm.LimitMode, alarm.Limit }.Where(part => part is { Length: > 0 }));

    private static void WriteScreens(TiaHmiDevice device, TextWriter output) {
        output.WriteLine();
        output.WriteLine("### Screens");
        if (device.Screens.Count == 0) {
            output.WriteLine();
            output.WriteLine("None.");
        }
        foreach (var screen in device.Screens) {
            output.WriteLine();
            output.WriteLine($"#### {(screen.Number is { } number ? number.ToString(CultureInfo.InvariantCulture) + " " : "")}{Path(screen.Folder, screen.Name)}{(screen.Width is { } width && screen.Height is { } height ? Invariant($" ({width} x {height})") : "")}");
            output.WriteLine();
            if (screen.Items.Count == 0) {
                output.WriteLine("No items.");
                continue;
            }
            output.WriteLine("| Item | Type | Left | Top | Width | Height | HMI tags |");
            output.WriteLine("|---|---|---|---|---|---|---|");
            foreach (var item in screen.Items) {
                output.WriteLine($"| {Cell(item.Name)} | {Cell(item.Type)} | {Number(item.Left)} | {Number(item.Top)} | {Number(item.Width)} | {Number(item.Height)} | {Cell(string.Join(", ", item.Tags))} |");
            }
        }
    }

    /// <summary>The report as JSON.</summary>
    public static void WriteJson(TiaHmiReport report, Stream output) {
        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteStartArray("devices");
        foreach (var device in report.Devices) {
            json.WriteStartObject();
            json.WriteString("name", device.Name);
            json.WriteString("family", device.FamilyName);
            json.WriteString("deviceType", device.DeviceType);
            json.WriteStartArray("connections");
            foreach (var connection in device.Connections) {
                json.WriteStartObject();
                json.WriteString("name", connection.Name);
                json.WriteString("plc", connection.Plc);
                json.WriteString("driver", connection.DriverName);
                json.WriteString("driverId", connection.Driver);
                json.WriteBoolean("integrated", connection.IsIntegrated);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("tagTables");
            foreach (var table in device.TagTables) {
                json.WriteStartObject();
                json.WriteString("name", table.Name);
                json.WriteString("folder", table.Folder);
                json.WriteStartArray("tags");
                foreach (var tag in table.Tags) {
                    json.WriteStartObject();
                    json.WriteString("name", tag.Name);
                    json.WriteString("dataType", tag.DataType);
                    json.WriteString("connection", tag.Connection);
                    json.WriteString("acquisitionCycle", tag.AcquisitionCycle);
                    json.WriteString("address", tag.Address);
                    if (tag.PlcLink is { } link) {
                        json.WriteStartObject("plcLink");
                        json.WriteString("plc", link.Plc);
                        json.WriteString(link.IsDataBlock ? "dataBlock" : "plcTag", link.Name);
                        if (link.IsDataBlock) {
                            json.WriteString("member", link.Member);
                            json.WriteBoolean("resolved", link.IsResolved);
                        }
                        json.WriteString("display", link.Display);
                        json.WriteEndObject();
                    } else {
                        json.WriteNull("plcLink");
                    }
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("alarms");
            foreach (var alarm in device.Alarms) {
                json.WriteStartObject();
                json.WriteString("kind", Kind(alarm));
                json.WriteString("name", alarm.Name);
                Number(json, "number", alarm.Number);
                json.WriteString("class", alarm.AlarmClass);
                json.WriteString("triggerTag", alarm.TriggerTag);
                if (alarm.Kind == TiaHmiAlarmKind.Discrete) {
                    Number(json, "bit", alarm.Bit);
                } else {
                    json.WriteString("limitMode", alarm.LimitMode);
                    json.WriteString("limit", alarm.Limit);
                }
                json.WriteString("text", alarm.Text);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("screens");
            foreach (var screen in device.Screens) {
                json.WriteStartObject();
                json.WriteString("name", screen.Name);
                json.WriteString("folder", screen.Folder);
                Number(json, "number", screen.Number);
                Number(json, "width", screen.Width);
                Number(json, "height", screen.Height);
                json.WriteStartArray("items");
                foreach (var item in screen.Items) {
                    json.WriteStartObject();
                    json.WriteString("name", item.Name);
                    json.WriteString("type", item.Type);
                    Number(json, "left", item.Left);
                    Number(json, "top", item.Top);
                    Number(json, "width", item.Width);
                    Number(json, "height", item.Height);
                    json.WriteStartArray("tags");
                    foreach (var tag in item.Tags) {
                        json.WriteStringValue(tag);
                    }
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            if (device.Code is { } code) {
                WriteCode(code, json);
            }
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteCode(TiaHmiCode code, Utf8JsonWriter json) {
        json.WriteStartArray("scripts");
        foreach (var script in code.Scripts) {
            json.WriteStartObject();
            json.WriteString("screen", script.Screen);
            json.WriteString("item", script.Item);
            json.WriteString(script.Kind == TiaHmiScriptKind.Event ? "event" : "property", script.Name);
            json.WriteString("language", script.Language);
            json.WriteString("code", script.Code);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartArray("scriptModules");
        foreach (var module in code.Modules) {
            json.WriteStartObject();
            json.WriteString("name", module.Name);
            json.WriteString("language", module.Language);
            json.WriteStartArray("functions");
            foreach (var function in module.Functions) {
                json.WriteStringValue(function);
            }
            json.WriteEndArray();
            json.WriteString("code", module.Code);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartArray("vbScripts");
        foreach (var script in code.VbScripts) {
            json.WriteStartObject();
            json.WriteString("name", script.Name);
            json.WriteString("folder", script.Folder);
            json.WriteString("returnType", script.ReturnType);
            json.WriteStartArray("parameters");
            foreach (var parameter in script.Parameters) {
                json.WriteStartObject();
                json.WriteString("name", parameter.Name);
                json.WriteString("declaration", parameter.Declaration);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteString("preCode", script.PreCode);
            json.WriteString("code", script.Code);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartArray("customWebControls");
        foreach (var control in code.CustomWebControls) {
            json.WriteStartObject();
            json.WriteString("guid", control.Guid);
            json.WriteString("name", control.DisplayName);
            json.WriteString("package", control.PackagePath);
            json.WriteStartArray("usedBy");
            foreach (var (screen, item) in control.UsedBy) {
                json.WriteStartObject();
                json.WriteString("screen", screen);
                json.WriteString("item", item);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("files");
            foreach (var file in control.Files) {
                json.WriteStartObject();
                json.WriteString("path", file.Path);
                json.WriteNumber("size", file.Content.Length);
                if (file.Text is { } text) {
                    json.WriteString("text", text);
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    /// <summary>
    /// A dump's files of one HMI device, by their path in its folder: <c>device.txt</c>, <c>connections.tsv</c>,
    /// <c>alarms.tsv</c>, one <c>tags/&lt;table&gt;.tsv</c> per tag table and one <c>screens/&lt;screen&gt;.tsv</c> per screen.
    /// </summary>
    public static IEnumerable<(string Path, string Text)> DumpFiles(TiaHmiDevice device) {
        yield return ("device.txt", $"name\t{device.Name}\nfamily\t{device.FamilyName}\ntype\t{device.DeviceType}\n");
        yield return ("connections.tsv", Tsv(["connection", "plc", "driver"], device.Connections.Select(connection => new[] { connection.Name, connection.Plc, connection.DriverName })));
        yield return ("alarms.tsv", Tsv(["kind", "number", "alarm", "class", "trigger tag", "raised by", "text"],
            device.Alarms.Select(alarm => new[] { Kind(alarm), Number(alarm.Number), alarm.Name, alarm.AlarmClass, alarm.TriggerTag, RaisedBy(alarm), alarm.Text })));
        foreach (var table in device.TagTables.Where(table => table.Tags.Count != 0)) {
            yield return (System.IO.Path.Combine("tags", PathOf(table.Folder, table.Name) + ".tsv"), Tsv(["hmi tag", "type", "connection", "cycle", "reads"],
                table.Tags.Select(tag => new[] { tag.Name, tag.DataType, tag.Connection ?? "internal", tag.AcquisitionCycle, Reads(tag) })));
        }
        foreach (var screen in device.Screens) {
            yield return (System.IO.Path.Combine("screens", PathOf(screen.Folder, screen.Name) + ".tsv"),
                $"# screen {Number(screen.Number)} {screen.Name} {Number(screen.Width)} x {Number(screen.Height)}\n" +
                Tsv(["item", "type", "left", "top", "width", "height", "hmi tags"],
                    screen.Items.Select(item => new[] { item.Name, item.Type, Number(item.Left), Number(item.Top), Number(item.Width), Number(item.Height), string.Join(", ", item.Tags) })));
        }
    }

    /// <summary>
    /// A dump's files of one HMI device's code (spec 0017), by their path in its folder:
    /// <c>scripts/&lt;screen&gt;/&lt;item&gt;.&lt;event or property&gt;.js</c> (<c>_screen</c> for the screen's own),
    /// <c>modules/&lt;name&gt;.js</c>, <c>vbscripts/&lt;name&gt;.vbs</c> and
    /// <c>custom web controls/&lt;name&gt;/&lt;file&gt;</c> with each package's files as stored.
    /// </summary>
    public static IEnumerable<(string Path, byte[] Content)> CodeFiles(TiaHmiDevice device) {
        if (device.Code is not { } code) {
            yield break;
        }
        foreach (var script in code.Scripts) {
            yield return (System.IO.Path.Combine("scripts", TiaSourceExport.Safe(script.Screen), TiaSourceExport.Safe((script.Item ?? "_screen") + "." + script.Name + ".js")),
                Encoding.UTF8.GetBytes(script.Code));
        }
        foreach (var module in code.Modules) {
            yield return (System.IO.Path.Combine("modules", TiaSourceExport.Safe(module.Name) + ".js"), Encoding.UTF8.GetBytes(module.Code));
        }
        foreach (var script in code.VbScripts) {
            yield return (System.IO.Path.Combine("vbscripts", PathOf(script.Folder ?? "", script.Name) + ".vbs"), Encoding.UTF8.GetBytes(VbSource(script)));
        }
        foreach (var control in code.CustomWebControls) {
            var folder = System.IO.Path.Combine("custom web controls", TiaSourceExport.Safe(control.DisplayName ?? control.Guid));
            foreach (var file in control.Files) {
                yield return (System.IO.Path.Combine([folder, .. file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(TiaSourceExport.Safe)]), file.Content);
            }
        }
    }

    private static string PathOf(string folder, string name) =>
        System.IO.Path.Combine([.. folder.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(TiaSourceExport.Safe), TiaSourceExport.Safe(name)]);

    private static string Tsv(string[] header, IEnumerable<string?[]> rows) {
        var text = new StringBuilder();
        text.Append(string.Join('\t', header)).Append('\n');
        foreach (var row in rows) {
            text.Append(string.Join('\t', row.Select(field => (field ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace("\r", "", StringComparison.Ordinal)))).Append('\n');
        }
        return text.ToString();
    }

    private static string Kind(TiaHmiAlarm alarm) => alarm.Kind == TiaHmiAlarmKind.Discrete ? "discrete" : "analog";

    private static string Path(string folder, string name) => folder.Length == 0 ? name : folder + "/" + name;

    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static void Number(Utf8JsonWriter json, string name, long? value) {
        if (value is { } number) {
            json.WriteNumber(name, number);
        } else {
            json.WriteNull(name);
        }
    }

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ');

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
