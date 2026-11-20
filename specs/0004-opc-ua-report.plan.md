---
type: Plan
title: "Plan 0004: OPC UA report"
description: "How spec 0004 is built in src/TiaFormat and src/TiaDump, and where it is tested."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
---
# Plan 0004: OPC UA report

## Modules

- `Storage/TiaDatabase`: dictionary expando reads by type. The key table already held each key's value type; it is kept now (`TiaExpandoKey`), so `ReadExpandoKeys` lists an object's keys and `ReadExpandoString`, `ReadExpandoInteger` and `ReadExpandoDateTime` read values the old reads could not. `ReadExpandoBlob` now reads only blob and XML keys. `ReadOptionalBoolean` tells an absent attribute from false. This replaces the scratch `DebugExpandos`.
- `Storage/NrbfReader`: an internal reader of the .NET binary serialization format (MS-NRBF) into plain objects, arrays, strings and primitives. It creates no type the stream names. TIA stores `UsersAndRolesStorage` and `CompiledInterfaceBlob` this way.
- `Program/TiaProgram`: `TiaMember.ExternalAccess` from the `MFlags` rule that already writes `ExternalAccessible`, and `TiaTag.ExternalAccess` and `ExternalVisible` from the tag's `IStructureItem` flags.
- `Program/TiaOpcUa`: finds the project's PLCs (`S7ControllerTargetData` in `ProjectData`), each one's OPC UA device item (`ConfigObjectTypeName` with `\OPC_UA\`), its `OpcUaServerConfigurationData` and `OpcUaInterfaceData` through `Target`, and its data blocks and tag tables, into a `TiaOpcUaReport`.
- `Program/TiaOpcUaWriter`: Markdown and JSON (`Utf8JsonWriter`, which needs no reflection and so suits Native AOT).
- `Program/TiaSecrets`: the one rule for what is never shown (FR-012), used by `TiaOpcUa` and `TiaInspector`.
- `Program/TiaInspector`: prints dictionary expando values, marked `~`, and withholds secrets.
- `src/TiaDump`: the `opcua` command with `--json` and `--plc`.

## Tests

`tests/TiaFormat.Tests/TiaOpcUaTest.cs` builds a small file with `TiaFileBuilder`: a project, two PLCs, OPC UA device items, a server storage with a serialized roles container, data blocks and a tag table. One test per acceptance scenario, named after it. The binary format reader is internal and is tested through the roles it reads: the test's roles container uses the record kinds TIA's do (a library, classes with member types, a class reused by id, strings, references, an object array with a null run). FR-015 was checked by dumping the three local archives with `main` and with this branch: the files are identical. That needs the archives, so it is not a test here. The `--plc` and `--json` options (FR-011, FR-016) are in `src/TiaDump`, which has no test project; they were checked by running the command.
