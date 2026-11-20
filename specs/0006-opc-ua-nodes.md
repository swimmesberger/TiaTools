---
type: Specification
title: "Spec 0006: OPC UA nodes and node set export"
description: "Which nodes a PLC's standard SIMATIC OPC UA server interface offers, derived from the program, listed in the viewer and tiadump and exported as a node set file."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
---
# Spec 0006: OPC UA nodes and node set export

## Intent

Someone who wants to read a PLC over OPC UA learns from the archive alone which node ids to ask for: every data block, member and PLC tag the standard SIMATIC server interface offers, with its node id, browse path, data type and whether it can be written. They see the list in the viewer's OPC UA view and in `tiadump`, and can export it as a node set file for configuring a client offline, the way TIA Portal's *OPC UA › Server › Export* does. The list is derived from the program by the rules in the [research note](../docs/research/2026-11-20-tia-opc-ua-server-settings.md), not read from TIA, and says so.

## User scenarios

### See which nodes a PLC offers (P1)

- Given a data block open to OPC UA with members open to outside access, when the nodes are listed, then the data block and each member are a node, with node id `ns=3;s="Block"."Member"`, browse path, SIMATIC and OPC UA data type, and read-write or read-only.
- Given a member closed to outside access, or under a closed one, when the nodes are listed, then it has no node.
- Given a data block closed to OPC UA, or one whose members are all closed, when the nodes are listed, then it has no node.
- Given a global and an instance data block, when the nodes are listed, then they are under *DataBlocksGlobal* and *DataBlocksInstance*.
- Given PLC tags that are accessible, when the nodes are listed, then each is a node `ns=3;s="Tag"` under a folder by its address area; a tag that is not accessible has none.
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

- Given the node set file, when it is read as an OPC UA node set, then it is valid UANodeSet XML with the namespace `http://www.siemens.com/simatic-s7-opcua`, an object per data block, and a variable per member and tag with its data type and access level.
- Given the file, when it is read, then its nodes whose id TiaTools does not know, the PLC object and its folders, are in a namespace of TiaTools' own, so no id is invented in Siemens' namespace.

## Requirements

- FR-001: The node list MUST hold, per PLC of the project, a node for each data block open to OPC UA that has at least one member open to outside access, and for each such member and its open members below it.
- FR-002: A member's node id MUST be `ns=3;s=` followed by the quoted names of the data block and the member path, joined by dots; a tag's MUST be `ns=3;s=` followed by its quoted name.
- FR-003: A node MUST state its browse path under the PLC's object, its SIMATIC data type, its OPC UA data type where the research note's table maps it, and whether it is read-write or read-only.
- FR-004: Global data blocks MUST be under *DataBlocksGlobal*, instance data blocks under *DataBlocksInstance*, and tags under *Inputs*, *Outputs*, *Memory*, *Timers* or *Counters* by their address area.
- FR-005: An array MUST be one node; its elements and the members of its elements MUST NOT be listed.
- FR-006: A know-how protected data block open to OPC UA MUST be listed, marked as having members that are not known, with no member nodes.
- FR-007: Every list and export MUST say it is derived from the program, not read from TIA, and that the namespace index is the default.
- FR-008: The viewer's OPC UA view MUST have a Nodes tab with the list; opening a row MUST show its data block or tag table; Copy node id MUST copy the node id.
- FR-009: The Nodes tab MUST offer Export node set, which writes the node set file of the PLC to a file the person picks.
- FR-010: `tiadump opcua --nodes` MUST write the nodes as tab-separated text with a header line; `--nodeset` MUST write the node set file of the one PLC that `--plc` names, and exit with 1 if the archive has more than one PLC and none is named.
- FR-011: The node set file MUST be UANodeSet XML (OPC UA Part 6), with `http://www.siemens.com/simatic-s7-opcua` for the data block objects, member and tag variables, and a namespace of TiaTools' own for the PLC object and folders.
- FR-012: A variable in the node set MUST carry its data type where mapped, its access level (3 read-write, 1 read-only), and its browse and display name.
- FR-013: The node list MUST be made when first asked for, in the viewer as in `tiadump`.

## Success criteria

- SC-001: A person can configure an OPC UA client for a PLC's data from the archive alone, needing TIA or the live PLC only to confirm.
- SC-002: Node ids in the list match what a client browses on the PLC, for the data blocks and tags it lists; open question 6 of the format page says how to confirm it.

## Out of scope

- Server interfaces and companion specifications: none of the samples has one, and their compiled form is not decoded.
- Methods, timers and counters' own folders' content beyond PLC tags, technology objects, the device properties under the PLC's object.
- Array elements as nodes (TIA's option "Export all array elements as separate nodes"), and the members of a tag of a UDT type.
- The data type nodes of UDTs and structures in the node set: a structured variable gets no `DataType` there.
- The node ids of the PLC object and its folders in Siemens' namespace, which the sources do not give.

## Decisions

None new. The rules come from the research note; the node set is written with the XML writer of the base class library.
