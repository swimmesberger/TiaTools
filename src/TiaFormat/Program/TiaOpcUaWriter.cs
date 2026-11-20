using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TiaFormat.Program;

/// <summary>Writes a <see cref="TiaOpcUaReport"/> as a Markdown report for people, or as JSON for programs.</summary>
public static class TiaOpcUaWriter {
    private const int ListedMembers = 10;

    /// <summary>The report's title for an archive: its file or folder name.</summary>
    public static string Title(string archive) => Path.GetFileName(Path.TrimEndingDirectorySeparator(archive));

    /// <summary>The Markdown report: per PLC its server, storage, roles, data blocks and tags.</summary>
    public static void WriteMarkdown(TiaOpcUaReport report, string title, TextWriter output) {
        output.Write("# OPC UA: ");
        output.WriteLine(title);
        if (report.Plcs.Count == 0) {
            output.WriteLine();
            output.WriteLine("The project has no PLC.");
        }
        foreach (var plc in report.Plcs) {
            WritePlc(plc, output);
        }
    }

    private static void WritePlc(TiaOpcUaPlc plc, TextWriter output) {
        output.WriteLine();
        output.Write("## PLC ");
        output.WriteLine(plc.Name);
        output.WriteLine();
        var blocks = plc.DataBlocks.Count(block => block.AccessibleFromOpcUa);
        var tags = plc.TagTables.SelectMany(table => table.Tags).ToList();
        var accessibleTags = tags.Count(tag => tag.ExternalAccess is TiaExternalAccess.ReadWrite or TiaExternalAccess.ReadOnly);
        var reach = Invariant($"{blocks} of {plc.DataBlocks.Count} data blocks are open to OPC UA, {accessibleTags} of {tags.Count} tags are accessible.");
        output.WriteLine(plc.Server switch {
            null => "**Server: no OPC UA device item.** " + reach,
            { IsEnabled: false } => "**Server: off.** No client reaches this PLC through its own OPC UA server. Were it on: " + reach,
            _ => "**Server: on.** " + reach,
        });
        if (plc.Server is { } server) {
            WriteServer(server, output);
        }
        if (plc.Storage is { } storage) {
            WriteStorage(storage, output);
        }
        WriteDataBlocks(plc.DataBlocks, output);
        WriteTags(plc.TagTables, output);
    }

    private static void WriteServer(TiaOpcUaServer server, TextWriter output) {
        output.WriteLine();
        output.WriteLine("| Setting | Value |");
        output.WriteLine("|---|---|");
        Row(output, "Device item", server.ConfigType is null ? Cell(server.DeviceItem) : Cell(server.DeviceItem) + " (`" + server.ConfigType + "`)");
        Row(output, "Server enabled", server.IsEnabled ? "yes" : "no");
        Row(output, "Application name", Stored(server.ApplicationName));
        Row(output, "Security policies", string.Join(", ", server.SecurityPolicies.Select(policy =>
            "`" + policy.Key + "` " + policy.IsEnabled switch { true => "on", false => "off", null => "not stored" })));
        Row(output, "Accept client certificates automatically", server.AutoAcceptClientCertificates switch { true => "yes", false => "no", null => "not stored" });
        Row(output, "Minimum publishing interval", server.MinimumPublishingInterval is { } interval ? Invariant($"{interval} ms") : "not stored");
        Row(output, "Server certificate", server.ServerCertificateAssigned ? "assigned" : "none");
        Row(output, "Trusted client certificates", server.TrustedClientCertificates.ToString(CultureInfo.InvariantCulture));
        Row(output, "Other stored settings", server.OtherSettings.Count == 0 ? "none" : string.Join(", ", server.OtherSettings.Select(setting => "`" + setting.Name + "` = " + Cell(setting.Value))));
        output.WriteLine();
        output.WriteLine("A setting that is not stored has TIA's default. Port, guest access and user authentication are stored only when changed, under names not yet seen. Which OPC UA policy each security key means is not confirmed (see the format page).");
    }

    private static void WriteStorage(TiaOpcUaStorage storage, TextWriter output) {
        output.WriteLine();
        output.WriteLine("### Server interfaces");
        output.WriteLine();
        output.WriteLine(storage.ServerInterfaces.Count == 0
            ? "No server interface objects."
            : "Server interfaces: " + string.Join(", ", storage.ServerInterfaces.Select(Cell)) + ".");
        output.WriteLine(Invariant($"Structure journal: {Journal(storage.StructureJournal, storage.HasStructureChanges)}. Mapping journal: {Journal(storage.MappingJournal, storage.HasMappingChanges)}."));
        output.WriteLine(Invariant($"Last compiled: {Date(storage.LastCompiled)}. Last downloaded: {Date(storage.LastDownloaded)}. Compiled interface: {storage.CompiledInterfaceBytes} bytes, not decoded."));
        output.WriteLine();
        output.WriteLine("### Roles");
        output.WriteLine();
        if (storage.RolesError is { } error) {
            output.WriteLine("The roles could not be read: " + error);
        } else if (storage.Roles.Count == 0) {
            output.WriteLine("No roles stored.");
        } else {
            output.WriteLine("| Role | Project role | Imported | Permissions |");
            output.WriteLine("|---|---|---|---|");
            foreach (var role in storage.Roles) {
                var permissions = string.Join("<br>", role.Permissions.Select(permission =>
                    (permission.NamespaceUri.Length == 0 ? "(local)" : permission.NamespaceUri) + ": " + (permission.Bits == 0 ? "none" : string.Join(", ", permission.Names))));
                output.WriteLine($"| {Cell(role.Name)} | {Cell(role.ProjectRole ?? "")} | {(role.IsImported ? "yes" : "no")} | {permissions} |");
            }
            output.WriteLine();
            output.WriteLine(Invariant($"Role assignments to project roles: {storage.RoleMappings}. Users stay in the project's user management, which this reader leaves alone."));
        }
    }

    private static void WriteDataBlocks(IReadOnlyList<TiaOpcUaDataBlock> blocks, TextWriter output) {
        output.WriteLine();
        output.WriteLine("### Data blocks");
        output.WriteLine();
        if (blocks.Count == 0) {
            output.WriteLine("None.");
            return;
        }
        output.WriteLine("| Number | Name | Kind | OPC UA | Members | Closed to outside access | Read-only |");
        output.WriteLine("|---|---|---|---|---|---|---|");
        foreach (var block in blocks) {
            output.WriteLine(Invariant($"| {block.Number?.ToString(CultureInfo.InvariantCulture) ?? ""} | {Cell(block.Name)} | {block.Kind} | {(block.AccessibleFromOpcUa ? "yes" : "no")} | {(block.IsClosed ? "closed" : block.Members.ToString(CultureInfo.InvariantCulture))} | {block.HiddenMembers.Count} | {block.ReadOnlyMembers.Count} |"));
        }
        var details = blocks.Where(block => block.AccessibleFromOpcUa && (block.HiddenMembers.Count != 0 || block.ReadOnlyMembers.Count != 0)).ToList();
        if (details.Count == 0) {
            return;
        }
        output.WriteLine();
        output.WriteLine("Members of open data blocks that a client cannot read, or cannot write; what is under them is included:");
        output.WriteLine();
        foreach (var block in details) {
            Members(block, block.HiddenMembers, "closed", output);
            Members(block, block.ReadOnlyMembers, "read-only", output);
        }

        static void Members(TiaOpcUaDataBlock block, IReadOnlyList<string> members, string what, TextWriter output) {
            foreach (var member in members.Take(ListedMembers)) {
                output.WriteLine($"- {Cell(block.Name)}.{Cell(member)}: {what}");
            }
            if (members.Count > ListedMembers) {
                output.WriteLine(Invariant($"- {Cell(block.Name)}: {members.Count - ListedMembers} more {what}, listed in the JSON report"));
            }
        }
    }

    private static void WriteTags(IReadOnlyList<TiaOpcUaTagTable> tables, TextWriter output) {
        output.WriteLine();
        output.WriteLine("### Tags");
        output.WriteLine();
        var used = tables.Where(table => table.Tags.Count != 0).ToList();
        if (used.Count == 0) {
            output.WriteLine("None.");
            return;
        }
        output.WriteLine("| Table | Tags | Read and write | Read only | Not accessible | Not stored |");
        output.WriteLine("|---|---|---|---|---|---|");
        foreach (var table in used) {
            output.WriteLine(Invariant($"| {Cell(table.Name)} | {table.Tags.Count} | {Count(table, TiaExternalAccess.ReadWrite)} | {Count(table, TiaExternalAccess.ReadOnly)} | {Count(table, TiaExternalAccess.None)} | {Count(table, null)} |"));
        }
        output.WriteLine();
        output.WriteLine("A tag's access is inferred from what it stores and not yet confirmed against TIA (see the format page): check a tag that matters in TIA's tag table.");
        var limited = used.SelectMany(table => table.Tags.Where(tag => tag.ExternalAccess is TiaExternalAccess.None or TiaExternalAccess.ReadOnly).Select(tag => (table, tag))).ToList();
        if (limited.Count == 0) {
            return;
        }
        output.WriteLine();
        output.WriteLine("Tags a client cannot read, or cannot write:");
        output.WriteLine();
        output.WriteLine("| Table | Tag | Type | Address | Access | Visible |");
        output.WriteLine("|---|---|---|---|---|---|");
        foreach (var (table, tag) in limited) {
            output.WriteLine($"| {Cell(table.Name)} | {Cell(tag.Name)} | {Cell(tag.Type ?? "")} | {Cell(tag.Address ?? "")} | {(tag.ExternalAccess == TiaExternalAccess.None ? "not accessible" : "read-only")} | {tag.ExternalVisible switch { true => "yes", false => "no", null => "" }} |");
        }

        static int Count(TiaOpcUaTagTable table, TiaExternalAccess? access) => table.Tags.Count(tag => tag.ExternalAccess == access);
    }

    /// <summary>The report as JSON, every data block and tag included.</summary>
    public static void WriteJson(TiaOpcUaReport report, Stream output) {
        using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteStartArray("plcs");
        foreach (var plc in report.Plcs) {
            json.WriteStartObject();
            json.WriteString("name", plc.Name);
            if (plc.Server is { } server) {
                json.WriteStartObject("server");
                json.WriteString("deviceItem", server.DeviceItem);
                json.WriteString("configType", server.ConfigType);
                json.WriteBoolean("enabled", server.IsEnabled);
                json.WriteString("applicationName", server.ApplicationName);
                json.WriteStartObject("securityPolicies");
                foreach (var policy in server.SecurityPolicies) {
                    Nullable(json, policy.Key, policy.IsEnabled);
                }
                json.WriteEndObject();
                Nullable(json, "autoAcceptClientCertificates", server.AutoAcceptClientCertificates);
                if (server.MinimumPublishingInterval is { } interval) {
                    json.WriteNumber("minimumPublishingIntervalMs", interval);
                } else {
                    json.WriteNull("minimumPublishingIntervalMs");
                }
                json.WriteBoolean("serverCertificateAssigned", server.ServerCertificateAssigned);
                json.WriteNumber("trustedClientCertificates", server.TrustedClientCertificates);
                json.WriteStartObject("otherSettings");
                foreach (var setting in server.OtherSettings) {
                    json.WriteString(setting.Name, setting.Value);
                }
                json.WriteEndObject();
                json.WriteEndObject();
            } else {
                json.WriteNull("server");
            }
            if (plc.Storage is { } storage) {
                json.WriteStartObject("storage");
                Date(json, "lastCompiled", storage.LastCompiled);
                Date(json, "lastDownloaded", storage.LastDownloaded);
                json.WriteBoolean("structureChanges", storage.HasStructureChanges);
                json.WriteBoolean("mappingChanges", storage.HasMappingChanges);
                json.WriteNumber("compiledInterfaceBytes", storage.CompiledInterfaceBytes);
                json.WriteStartArray("serverInterfaces");
                foreach (var name in storage.ServerInterfaces) {
                    json.WriteStringValue(name);
                }
                json.WriteEndArray();
                json.WriteStartArray("roles");
                foreach (var role in storage.Roles) {
                    json.WriteStartObject();
                    json.WriteString("name", role.Name);
                    json.WriteString("projectRole", role.ProjectRole);
                    json.WriteBoolean("imported", role.IsImported);
                    json.WriteStartArray("permissions");
                    foreach (var permission in role.Permissions) {
                        json.WriteStartObject();
                        json.WriteString("namespace", permission.NamespaceUri);
                        json.WriteNumber("bits", permission.Bits);
                        json.WriteStartArray("names");
                        foreach (var name in permission.Names) {
                            json.WriteStringValue(name);
                        }
                        json.WriteEndArray();
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteNumber("roleMappings", storage.RoleMappings);
                json.WriteString("rolesError", storage.RolesError);
                json.WriteEndObject();
            } else {
                json.WriteNull("storage");
            }
            json.WriteStartArray("dataBlocks");
            foreach (var block in plc.DataBlocks) {
                json.WriteStartObject();
                json.WriteString("name", block.Name);
                json.WriteString("kind", block.Kind);
                if (block.Number is { } number) {
                    json.WriteNumber("number", number);
                } else {
                    json.WriteNull("number");
                }
                json.WriteBoolean("accessibleFromOpcUa", block.AccessibleFromOpcUa);
                json.WriteBoolean("closed", block.IsClosed);
                json.WriteNumber("members", block.Members);
                Strings(json, "hiddenMembers", block.HiddenMembers);
                Strings(json, "readOnlyMembers", block.ReadOnlyMembers);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("tagTables");
            foreach (var table in plc.TagTables) {
                json.WriteStartObject();
                json.WriteString("name", table.Name);
                json.WriteStartArray("tags");
                foreach (var tag in table.Tags) {
                    json.WriteStartObject();
                    json.WriteString("name", tag.Name);
                    json.WriteString("type", tag.Type);
                    json.WriteString("address", tag.Address);
                    json.WriteString("access", tag.ExternalAccess switch {
                        TiaExternalAccess.ReadWrite => "readWrite",
                        TiaExternalAccess.ReadOnly => "readOnly",
                        TiaExternalAccess.None => "none",
                        _ => null,
                    });
                    Nullable(json, "visible", tag.ExternalVisible);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Nullable(Utf8JsonWriter json, string name, bool? value) {
        if (value is { } flag) {
            json.WriteBoolean(name, flag);
        } else {
            json.WriteNull(name);
        }
    }

    private static void Date(Utf8JsonWriter json, string name, DateTime? value) {
        if (value is { } date) {
            json.WriteString(name, date.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        } else {
            json.WriteNull(name);
        }
    }

    private static void Strings(Utf8JsonWriter json, string name, IReadOnlyList<string> values) {
        json.WriteStartArray(name);
        foreach (var value in values) {
            json.WriteStringValue(value);
        }
        json.WriteEndArray();
    }

    private static void Row(TextWriter output, string name, string value) => output.WriteLine($"| {name} | {value} |");

    private static string Stored(string? value) => value is null ? "not stored" : Cell(value);

    private static string Journal(string? journal, bool hasChanges) => journal is null ? "not stored" : hasChanges ? Invariant($"holds changes ({journal.Length} characters, not decoded)") : "empty";

    private static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "never";

    /// <summary>Text for a table cell: a pipe or line break would end the cell.</summary>
    private static string Cell(string text) {
        if (text.AsSpan().IndexOfAny('|', '\n', '\r') < 0) {
            return text;
        }
        var result = new StringBuilder(text.Length + 8);
        foreach (var c in text) {
            result.Append(c switch { '|' => "\\|", '\n' or '\r' => " ", _ => c.ToString() });
        }
        return result.ToString();
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
