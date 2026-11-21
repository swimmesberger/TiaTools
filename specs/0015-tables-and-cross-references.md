---
type: Specification
title: "Spec 0015: watch tables, force tables and TIA's cross references, read and checked against TIA Portal"
description: "The reader shows a PLC's watch and force tables and the cross references TIA keeps for the program, and a reference project built by TIA Portal checks both against TIA's own export in every test run."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# Spec 0015: watch tables, force tables and TIA's cross references, read and checked against TIA Portal

## Intent

Two things a person or a coding agent reading a PLC program without TIA Portal asks for are not answered today. The watch tables and the force table hold what the people who commission a machine look at and change online, the operands they care about and the values they set; the reader does not read them at all. And "who writes this tag, who calls this block" is answered only from names found in the text, which misses calls in LAD, FBD, STL and GRAPH networks, cannot tell a read from a write, points at a tag table instead of the tag, and says nothing about members of a block or what a closed block uses. TIA keeps its own cross-reference list in the archive, with every use, its access and its place. The reader reads that list, shows each watch and force table as text and in the viewer, and a new reference project, invented and built by TIA Portal, checks the tables and the cross references against TIA's own export in every test run.

## User scenarios

### Read a watch table (P1)

- Given a PLC with watch tables, when it is dumped, a table is shown with `tiadump show` or exported or copied from the viewer, then each table appears with its folder and name, and each entry in TIA's order with its name or address, display format, monitor trigger, modify value, whether the modify value is selected, modify trigger and comment.
- Given a watch table with a line that holds only a comment, then that line appears in its place.
- Given the viewer, when a project is opened, then the watch tables appear in the tree under their PLC, in their folders, and selecting one shows its text.

### Read the force table (P1)

- Given a PLC whose force table has entries, when it is dumped, shown or exported, then each entry appears with its name or address, display format, monitor trigger, force value, whether forcing is selected, and comment.
- Given a PLC whose force table is empty, then no file is written for it, as for an empty tag table.

### Find who uses an object (P1)

- Given a tag, a data block, a data block member, a block, or a member of a block's interface, when its cross references are asked for with `tiadump` or shown in the viewer, then each use is listed with the object that uses it, the access (such as read, write, call, instance data block, multi-instance, type), and the place: the network and its number, and the line of the text where the reader can tell it.
- Given a call in a LAD, FBD, STL or GRAPH network, including a call of a system instruction with its own data block and a multi-instance call, then it is listed as a call of the block it calls.
- Given a tag that a watch table or the force table names, then that table is among its uses, with the access Monitor, and Modify or Force where the entry has a value. TIA's list names no table, so these uses are the reader's own, named with TIA's words.
- Given the viewer shows a tag table, then the uses of each of its tags are listed with the tag they use.

### What a closed block uses (P1)

- Given a closed block that calls a block, reads or writes a tag or a data block, then its cross references list each such object with the access, and give no place inside the closed block.
- Given a closed block, then no member of its own interface is listed as read or written by it, and nothing of its body is shown, as before (ADR 0015).
- Given an open block that calls a closed one, then that call is listed like any other.

### An archive without TIA's list (P2)

- Given an archive with no cross-reference list, or with one that does not describe its current program, when cross references are shown, then they are the references found in the text, and the output says that they come from the text and may be incomplete.
- Given references found in the text, then a call in a LAD, FBD, STL or GRAPH network counts as a call, and a tag at the start of a line of statement form is found.

### The tests check against TIA Portal (P1)

- Given the reference project for these tables, when the library's tests run on a machine without TIA Portal, then every watch and force table entry the reader reads is compared with TIA's export of that table, and the cross references of every object with TIA's own cross-reference list.
- Given an entry or a cross reference that differs, when the tests run, then they fail and name the reference project, the table and entry or the objects, and both versions of what differs.
- Given the reference project's sources, when the maintainer builds it again, then TIA builds the project, compiles it, and exports its watch and force tables and its cross-reference list beside the generated source; a source or table TIA refuses is reported with TIA's error and the committed files stay unchanged.

### The reference project covers the tables and the uses (P2)

- Given the reference project, then together its watch tables hold entries on tags, on data block members, on an absolute address with no tag, in display formats other than the default, with a modify value and one without, with triggers other than the default, with a comment, and a comment line, and one table is in a folder; and its force table holds entries on an input and an output, each with a force value. Whether a value is selected cannot be set through TIA's import, so no reference project holds a selected one.
- Given the reference project's program, then it holds blocks in SCL, LAD, FBD, STL and GRAPH that read and write tags and data block members, call FCs, FBs as single and multi-instances and a system instruction, use a UDT as a type, and a closed block that calls another block and writes a tag.

## Requirements

- FR-001: The reader MUST read every watch table of a PLC, with its folder and name and each entry in TIA's order: name or address, display format, monitor trigger, modify value, whether the modify value is selected, modify trigger and comment; a line that holds only a comment MUST be read in its place.
- FR-002: The reader MUST read a PLC's force table, with each entry's name or address, display format, monitor trigger, force value, whether forcing is selected, and comment.
- FR-003: The dump, `tiadump show` and the viewer's export and copy MUST write each watch table and each force table with entries as one text file, listed in the dump's index; an empty table MUST NOT get a file.
- FR-004: The viewer MUST show the watch tables and the force table in the tree under their PLC, in their folders, as their text.
- FR-005: The reader MUST read TIA's cross-reference list from the archive whenever the archive holds one that describes its current program, without writing to the archive or unpacking it to disk.
- FR-006: Each cross reference MUST name the object used, the object that uses it, the access in TIA's terms, and the place: the network with its number, and the line of the reader's text where the stored form tells it.
- FR-007: The objects whose uses MUST be listed are blocks, tags, data blocks and their members, and the members of a block's interface; the objects that MUST be listed as using them are blocks, watch tables and the force table. The uses by tables come from their entries, since TIA's list holds none.
- FR-008: For a closed block the reader MUST list each other object it uses with the access, and MUST NOT list a place inside it or a use of a member of its own interface (ADR 0015).
- FR-009: When the archive holds no cross-reference list, or one that does not describe its current program, the reader MUST show the references it finds in the text instead, and every output that shows them MUST say that they come from the text and may be incomplete.
- FR-010: The references found in the text MUST count a call in a LAD, FBD, STL or GRAPH network as a call of the block it calls, including a call through a data block of its own and a multi-instance call, and MUST find a global name at the start of a line of statement form.
- FR-011: A `tiadump` command MUST list the cross references of a named object, or of all objects, as text with one use per line, giving what FR-006 names.
- FR-012: The viewer's lists of what an item uses and is used by MUST show each reference's access and lead to its place; for a tag table they MUST list the uses of each of its tags, naming the tag.
- FR-013: Reading the cross-reference list MUST NOT need anything beyond the base class library (ADR 0014).
- FR-014: A reference project MUST hold invented watch tables, a force table and a program that together cover every part listed in the seventh user story, built and compiled by TIA Portal V21.
- FR-015: Building a reference project MUST import its watch and force tables, and MUST export each watch and force table and TIA's cross-reference list of every block, tag, data block and table, as TIA gives them, beside the generated source.
- FR-016: The library's tests MUST compare every watch and force table entry of the reference projects with TIA's export, and the cross references of every object of a reference project that has TIA's list with that list, by object used, object using it, access and network, and fail on a difference naming the reference project, the objects or the table and entry, and both versions.
- FR-017: A difference between the reader and TIA that is understood and accepted MUST be listed with its reason, as spec 0008 lists known differences, and a listed difference that no longer occurs MUST fail the tests.

## Success criteria

- SC-001: Every watch and force table entry of the reference project reads as TIA exports it.
- SC-002: For every object of the reference project, the reader lists the same uses as TIA's cross-reference list, apart from the listed known differences.
- SC-003: A coding agent answers "what writes this tag" or "what calls this block" for a project with one `tiadump` command, without TIA Portal, and the answer includes LAD, FBD, STL and GRAPH networks.
- SC-004: After TIA Portal is no longer available, a change that breaks how the reader reads the tables or the cross references fails the tests in CI.

## Out of scope

- Monitoring, modifying or forcing values online, and writing a table back in a form TIA could import.
- TIA's assignment list and overlapping accesses: which bits of the I, Q and M areas are used, and by which tags.
- Uses from HMI screens, alarms, technology objects and the hardware configuration; the list may hold them, and they are read when those objects are.
- Watch and force tables kept in master copies and global libraries.
- Bringing an out-of-date cross-reference list up to date; that is TIA's compile.
- Decoding more of TIA's list than the requirements need.

## Decisions

- [ADR 0011](../docs/adr/0011-know-how-protection-stays-closed.md): protected blocks are read as encrypted.
- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md): reference projects are committed.
- [ADR 0014](../docs/adr/0014-xref-db-is-read-by-a-reader-of-our-own.md), new: TIA's cross-reference list, a SQLite file, is read by a read-only reader of that file format written here, not through a SQLite library.
- [ADR 0015](../docs/adr/0015-what-a-closed-block-uses-is-shown.md), new: what a closed block uses, as TIA's cross-reference list keeps it in clear, is shown; its body, its own members' uses and the places inside it are not.
