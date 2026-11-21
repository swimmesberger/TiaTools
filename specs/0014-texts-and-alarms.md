---
type: Specification
title: "Spec 0014: texts in every project language, program alarms and text lists"
description: "The reader shows a project's titles and comments in any of its project languages and lists all of them in a texts table, reads each PLC's program alarms, alarm classes and text lists, and a reference project with several languages checks all of it against TIA Portal."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# Spec 0014: texts in every project language, program alarms and text lists

## Intent

A project keeps every title and comment in each of its project languages, but the reader shows one, and it picks English first rather than the project's editing language, so a program commented in German alone or in two languages reads only partly. A PLC program also says much of what it means outside its code: its program alarms, the texts an operator sees with their associated values, the alarm classes, and the text lists those texts draw on. Someone reading a program without TIA Portal, a person or a coding agent, should read all of this: block sources in one project language, the editing language unless they choose another; every text of the PLC in every project language in one texts table; and each PLC's program alarms and text lists as tables of their own. A new reference project, invented, with three project languages, a text list and an FB with program alarms, checks every one of these texts against TIA Portal's own export in every test run.

## User scenarios

### Read a program in the project's own language (P1)

- Given a project whose editing language is not English, when it is dumped, shown with `tiadump show` or exported from the viewer, then every title and comment is in the editing language, and each SCL block, data block and UDT still equals TIA's generated source.
- Given a project with several project languages, when the user names one of them for the dump, `tiadump show` or the viewer, then every title and comment is in that language.
- Given a title or comment that has no text in the chosen language, then the reader shows its text in the reference language, or failing that in the first project language that has one, as it does today.
- Given a language that is not a project language, when the user names it, then the tool fails and names the project languages.
- Given an archive, when the user asks the tool for its languages, then it names every project language and marks the editing and the reference language.

### See every text in every language (P1)

- Given a project with several project languages, when it is dumped, then the dump holds a texts table for each PLC with one row per text and one column per project language: block titles and comments, network titles and comments, member comments, tag comments, text list entries, alarm texts and alarm class names.
- Given a row of the texts table, then it names the object the text belongs to and its place in it, such as a block's comment, network 3's title, a member's path or a tag's name, so that a reader finds the same text in the source.
- Given a text empty in some language, then that cell is empty: the texts table shows what is stored, with no fallback.

### Read a PLC's program alarms (P1)

- Given a PLC with a function block that calls `Program_Alarm`, when its alarms are listed with `tiadump alarms`, in the dump or in the viewer, then each alarm is listed with the block that declares it and its path there, each instance data block it has an instance in, its alarm class, its priority, and its alarm text, info text and additional texts in every project language.
- Given an alarm text with associated values, then the text is shown as stored, with its placeholders, and for each associated value the alarm lists the operand the block wires to it.
- Given an alarm text that takes a text from a text list, then the alarm names that text list.
- Given an alarm declared in a know-how protected block, then the alarm is listed as closed, and none of its texts appears anywhere in what the reader returns for the archive.

### Read alarm classes and text lists (P2)

- Given a project, when its alarm classes are listed, then each appears with its name, its display name in every project language, and whether it needs acknowledgement.
- Given a PLC with user text lists, when they are listed with `tiadump textlists`, in the dump or in the viewer, then each list appears with its name and range type, and each entry with its value or range and its text in every project language.
- Given a text list, then it also names every alarm whose text uses it.

### Browse texts and alarms in the viewer (P2)

- Given an archive open in the viewer, then a menu picks the language for every title and comment shown, by default the editing language, and export and copy write the language chosen.
- Given a PLC in the viewer's tree, then it holds its alarms and its text lists, each shown as a table in the chosen language.

### The tests check every text against TIA Portal (P1)

- Given the reference project for texts, when the library's tests run on a machine without TIA Portal, then every block, network and member text in every project language, every tag comment in every language, every text list entry and every alarm text are compared with TIA's own export of them, and the project languages with TIA's language settings.
- Given a text whose reading differs from TIA's export, when the tests run, then they fail and name the reference project, the object, the place, the language, and both versions.
- Given the reference project's sources, when the maintainer builds it again, then TIA builds the project with its languages, text lists and alarm texts, compiles it, and exports what the comparison needs; anything TIA refuses is reported with TIA's error and the committed files stay unchanged.

## Requirements

- FR-001: The reader MUST read a project's project languages, its editing language and its reference language, and the tool MUST name them, marking the editing and the reference language.
- FR-002: The reader MUST read every title and comment of a block, network, member and tag in each project language it is stored in.
- FR-003: The dump, `tiadump show` and the viewer's export and copy MUST write titles and comments in one language: the editing language, unless the user chooses another project language.
- FR-004: Where a title or comment has no text in the language written, the reader MUST take the reference language's, and failing that the first project language's that has one.
- FR-005: Naming a language that is not a project language MUST fail and name the project languages.
- FR-006: The dump MUST write a texts table per PLC: one row per text, naming the object and the place of the text in it, with one column per project language, in the order the project lists its languages, and the stored text, or nothing, in each.
- FR-007: The texts table MUST hold block titles and comments, network titles and comments, member comments, tag comments, text list entries, alarm texts and alarm class display names.
- FR-008: The reader MUST list each PLC's program alarms, each with the block that declares it, its path there, every instance data block that holds an instance of it, its alarm class, its priority, and its alarm text, info text and additional texts in every project language.
- FR-009: An alarm text MUST be shown as stored, with its placeholders for associated values and text lists.
- FR-010: For each associated value an alarm's texts use, the reader SHOULD name the operand its block wires to it.
- FR-011: The reader MUST name each text list an alarm's texts take a text from.
- FR-012: The reader MUST list the project's alarm classes with their name, their display name in every project language, and whether they need acknowledgement.
- FR-013: The reader MUST list each PLC's user text lists with their name and range type, and each entry with its value or range and its text in every project language.
- FR-014: For each text list the reader MUST name the alarms whose texts use it.
- FR-015: `tiadump alarms` and `tiadump textlists` MUST print a PLC's alarms and text lists, and the dump MUST write them as a table each per PLC.
- FR-016: The viewer MUST let the user choose the language of every title and comment it shows and exports, defaulting to the editing language.
- FR-017: The viewer MUST show each PLC's alarms and text lists in its tree, each as a table in the chosen language.
- FR-018: An alarm declared in a know-how protected block MUST be listed as closed, and none of its texts MUST appear in the dump, the tables, the search results or anything the viewer shows.
- FR-019: A reference project MUST hold, invented and built by TIA Portal V21: three project languages with an editing language other than English; block titles and comments, network titles and comments, member comments and tag comments set in each language, and one left empty in the editing language; a user text list with single values and a range; an FB with several `Program_Alarm` calls, alarm texts with associated values and a text from the text list, an info text, alarms of two alarm classes, and an instance data block; and a know-how protected FB with an alarm.
- FR-020: Building a reference project MUST set its project languages, import its text lists and alarm texts, and export, beside the generated source, TIA's own view of its languages, of every block text in every language, of its text lists and of its alarm texts.
- FR-021: The library's tests MUST compare every text FR-019 lists, in every language, with TIA's export, and fail on a difference naming the reference project, the object, the place, the language and both versions.
- FR-022: The library's tests MUST compare the generated source of the reference project's SCL blocks and data blocks with what the reader writes in the editing language, as for every reference project.

## Success criteria

- SC-001: Every title and comment of the reference project is readable in each of its three languages, and agrees with TIA's export in every test run.
- SC-002: A reader of a dump finds every text of a PLC in every project language without TIA Portal, and every program alarm and text list of the PLC.
- SC-003: A program commented only in a language other than English is dumped in that language, with source equal to TIA's.
- SC-004: After TIA Portal is no longer available, a change that breaks how the reader reads a text in any language, an alarm or a text list fails the tests in CI.

## Out of scope

- HMI alarms, HMI text lists and HMI texts: the HMI spec.
- System diagnostics alarms, system text lists, and the alarms of GRAPH supervisions and ProDiag.
- Alarms that are not `Program_Alarm`: `ALARM`, `NOTIFY` and the other alarm instructions of S7-300 and S7-400, and alarms of technology objects.
- Writing or changing texts, translating, and TIA's own project text export format.
- Comments inside SCL and STL code, which are code and have one language.
- Texts of the know-how protected block itself, which stay closed as ADR 0011 requires.
- The samples: whether they hold alarms or several languages may be counted, nothing more.

## Decisions

- [ADR 0011](../docs/adr/0011-know-how-protection-stays-closed.md): know-how protection stays closed, so an alarm of a protected block does too.
- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md): reference projects are committed, with TIA's own output.
