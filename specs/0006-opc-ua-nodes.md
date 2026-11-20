---
type: Specification
title: "Spec 0006: OPC UA nodes and node set export"
description: "Which nodes a PLC's standard SIMATIC OPC UA server interface offers, derived from the program, listed in the viewer and tiadump and exported as a node set file."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: stable
---
# Spec 0006: OPC UA nodes and node set export

## Intent

Someone who wants to read a PLC over OPC UA learns from the archive alone which node ids to ask for: every data block, member and PLC tag the standard SIMATIC server interface offers, with its node id, browse path, data type and whether it can be written. They see the list in the viewer's OPC UA view and in `tiadump`, and can export it as a node set file for configuring a client offline, the way TIA Portal's *OPC UA › Server › Export* does. The list is derived from the program by the rules that TIA Portal V21's own export follows (the [research note](../docs/research/2026-11-20-tia-opc-ua-server-settings.md)), not read from TIA, and says so.

## User scenarios

### See which nodes a PLC offers (P1)

- Given a data block open to OPC UA with members open to outside access, when the nodes are listed, then the data block is an object `ns=3;s="Block"` and each member a variable `ns=3;s="Block"."Member"`, with browse path, SIMATIC and OPC UA data type, and read-write or read-only.
- Given a member closed to outside access, or under a closed one, when the nodes are listed, then it has no node.
- Given a data block closed to OPC UA, or one whose members are all closed, when the nodes are listed, then it has no node.
- Given a global and an instance data block, when the nodes are listed, then they are under `ns=3;s=DataBlocksGlobal` and `ns=3;s=DataBlocksInstance`, below the PLC's object `ns=3;s=PLC`.
- Given an instance data block, when the nodes are listed, then its members are under objects for their sections (`Inputs`, `Outputs`, `InOuts`, `Static`), but their node ids leave the section out; a multi-instance is an object with sections of its own.
- Given a member of a UDT, a struct, DTL, an IEC timer or an IEC counter, when the nodes are listed, then it is a variable with its members below it as variables.
- Given an array, when the nodes are listed, then it is a variable with its dimensions, and each element is a variable `"Block"."Member"[0]`, counted from 0 in each dimension, with its members below it.
- Given a struct with a read-only member, when the nodes are listed, then the struct is read-only.
- Given PLC tags that are accessible, when the nodes are listed, then each is a variable `ns=3;s="Tag"` in the folder of its address area (`Inputs`, `Outputs`, `Memory`, `Timers`, `Counters`); a tag that is not accessible has none.
- Given a know-how protected data block open to OPC UA, when the nodes are listed, then the data block is a node, marked as having members that are not known.
- Given the list, when it is shown, then it says that it is derived from the program, that the namespace index 3 is the default and may differ, and that the server's real list comes from TIA's export.

### Use it from the viewer (P1)

- Given the OPC UA view, when the Nodes tab is shown, then it lists the nodes with node id, browse path, data type and access.
- Given a node row, when it is opened, then the viewer shows its data block or tag table, as the other lists do.
- Given a node row, when Copy node id is chosen, then its node id is copied.
- Given the Nodes tab, when Export node set is chosen and a file is picked, then the node set file is written there.

### Use it from the command line (P1)

- Given an archive, when `tiadump opcua --nodes` runs, then it writes one line per node as tab-separated text.
- Given an archive and a PLC, when `tiadump opcua --plc <PLC> --nodeset` runs, then it writes the PLC's node set file to standard output.
- Given an archive with several PLCs, when `--nodeset` runs without `--plc`, then it exits with 1 and says to name a PLC.

### Use the export in a client (P2)

- Given the node set file, when it is read as an OPC UA node set, then it is valid UANodeSet XML with the namespace `http://www.siemens.com/simatic-s7-opcua`, the PLC's object, its folders, an object per data block and section, and a variable per member, element and tag with its data type, dimensions and access level, as TIA's export has them.

## Requirements

- FR-001: The node list MUST hold, per PLC of the project, a node for each data block open to OPC UA that has at least one member open to outside access, and for each such member and its open members below it.
- FR-002: A member's node id MUST be `ns=3;s=` followed by the quoted names of the data block and the member path, joined by dots, with an array element as its zero-based position in brackets; a member of DTL MUST be named without quotes; a tag's node id MUST be `ns=3;s=` followed by its quoted name.
- FR-003: A node MUST state its browse path under the PLC's object, its SIMATIC data type, its OPC UA data type, and whether it is read-write or read-only.
- FR-004: Global data blocks MUST be under `DataBlocksGlobal`, instance data blocks under `DataBlocksInstance`, and tags under `Inputs`, `Outputs`, `Memory`, `Timers` or `Counters` by their address area, each folder `ns=3;s=<name>` under the PLC's object `ns=3;s=PLC`.
- FR-005: The members of an instance data block and of a multi-instance MUST be under an object per section, `<parent>.Inputs`, `.Outputs`, `.InOuts` or `.Static`, and their node ids MUST leave the section out.
- FR-006: An array MUST be a variable with its dimensions and its own access, and its elements MUST be variables below it, as TIA's export lists them whatever its option to export array elements as separate nodes says.
- FR-007: A struct, UDT, DTL, IEC timer or IEC counter member MUST be a variable with its members below it; it MUST be read-only if any member below it is.
- FR-008: A know-how protected data block open to OPC UA MUST be listed, marked as having members that are not known, with no member nodes.
- FR-009: Every list and export MUST say it is derived from the program, not read from TIA, and that the namespace index is the default.
- FR-010: The viewer's OPC UA view MUST have a Nodes tab with the list; opening a row MUST show its data block or tag table; Copy node id MUST copy the node id.
- FR-011: The Nodes tab MUST offer Export node set, which writes the node set file of the PLC to a file the person picks.
- FR-012: `tiadump opcua --nodes` MUST write the nodes as tab-separated text with a header line; `--nodeset` MUST write the node set file of the one PLC that `--plc` names, and exit with 1 if the archive has more than one PLC and none is named.
- FR-013: The node set file MUST be UANodeSet XML (OPC UA Part 6) whose node ids, browse names, parents, references, data types, value ranks, array dimensions and access levels are those TIA's export gives the same nodes.
- FR-014: The node list MUST be made when first asked for, in the viewer as in `tiadump`.

## Success criteria

- SC-001: A person can configure an OPC UA client for a PLC's data from the archive alone, needing TIA or the live PLC only to confirm.
- SC-002: For a test project, every node the list holds is in TIA's export with the same id, parent, data type and access, and every data block, member, element and tag node of TIA's export is in the list.

## Out of scope

- Server interfaces and companion specifications: none of the samples has one, and their compiled form is not decoded.
- Methods, technology objects, and the device properties under the PLC's object.
- The members of system types other than DTL, IEC timers and IEC counters of `Int`: their variable is listed, its members are not.
- The data type, object type and variable type nodes of UDTs, structs and FBs: the node set refers to them by TIA's ids but does not define them.
- What a running server offers beyond TIA's export: the list follows the export.

## Decisions

None new. The rules come from TIA Portal V21's export, recorded in the research note; the node set is written with the XML writer of the base class library.
