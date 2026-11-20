---
type: Plan
title: "Plan 0006: OPC UA nodes and node set export"
description: "How spec 0006 is built in src/TiaFormat, src/TiaDump and src/TiaViewer, how it was compared with TIA's export, and where it is tested."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
---
# Plan 0006: OPC UA nodes and node set export

## Modules

- `Program/TiaOpcUaNodes`: `Read(program, plc)` gives a `TiaOpcUaNodeList` per PLC of the project itself: the PLC's object and folders, then the accessible tags by address area, then the data blocks in number order (FR-001, FR-004). A data block open to OPC UA is an object; a global one's members, or those of the UDT it is made from, follow below it; an instance's members come from its FB, in section objects (FR-005). `Typed` derives a member's nodes from its type: an array with its elements (FR-006), a struct, a UDT, DTL, an IEC timer or counter with their members (FR-007), an FB as a multi-instance object, anything else as one variable. Access combines the member's own with its parent's; a closed member and what is under it give no node, and a data block, section or struct left without nodes is taken out again (FR-001). A know-how protected data block is an object marked `MembersUnknown` (FR-008). `WriteText` writes the tab-separated list after the note `Derived` (FR-009, FR-012). The PLCs and their OPC UA items come from `TiaOpcUa.Scan`, shared with the report.
- `Program/TiaOpcUaNodeSet`: `Write(list, stream)` writes UANodeSet XML with the base class library's `XmlWriter`, laid out as TIA's export is: namespace index 1 left empty so that `ns=3` is the Siemens namespace, aliases for the data and reference types used, and per node its browse name, parent, data type, access level, value rank, dimensions, display name, description and references (FR-013). A comment says it is derived (FR-009).
- `Program/TiaProgram`: an elementary type code that no identifier of the file names falls back on the table of the format page, section 11, so a member of such a type has its name, not `RID 0x...`; the node list needs it for its data types.
- `Program/TiaProject`: `OpcUaNodes(item)` derives a report's node list once, when first asked for (FR-014).
- `src/TiaDump`: `opcua --nodes` and `--nodeset`, with `--plc`; `--nodeset` exits with 1 unless exactly one PLC is chosen (FR-012).
- `src/TiaViewer/OpcUaView`: a Nodes tab, built when first selected from the function `MainWindow` hands it, with node id, data type and access; double-click or Enter opens the node's data block or tag table through `Open`, as the other lists do; Copy node id and Ctrl+C copy the node id; Export node set asks for a file and writes it (FR-010, FR-011). `Said` puts what was done in the status bar.

## Comparing with TIA

The rules were read off TIA Portal V21's export, through Openness's `OpcUaExportProvider`, of a copy of the demo project with test data blocks for every elementary type, arrays of one and two dimensions, structs, DTL, timers, counters and each combination of access. The comparison, node by node on id, browse name, parent, references, data type, value rank, dimensions, access level and description, is in the research note. The test blocks and the scripts stay outside the repository, as every archive does.

## Tests

`tests/TiaFormat.Tests/TiaOpcUaNodesTest.cs`, one per acceptance scenario that the library carries, on `TiaOpcUaFixture` with `HasTypes`: a UDT, two FBs (one a multi-instance in the other, with an IEC timer), a global data block with a UDT, an array of it, DTL, a struct with a read-only member and a two-dimensional array, one with every member closed, a know-how protected one and an instance data block. `TiaProjectOpcUaTest` covers FR-014 and opening a row. `TiaSourceFileTest` covers the type code fallback. The command line's choice of PLC and the Nodes tab's buttons were checked by hand, the tab rendered headless on the demo copy as plan 0005 did.
