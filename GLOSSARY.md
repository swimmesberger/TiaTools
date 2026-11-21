---
type: Glossary
title: "Glossary"
description: "The project's own terms, opinionated, with the words to avoid."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T12:00:00Z }
status: draft
---
# TiaTools

Reading Siemens TIA Portal projects, the files a PLC program is kept in, without TIA Portal.

This file is the glossary and nothing else: the project's own terms, one or two sentences each, opinionated, with the words to avoid. No general programming concepts, project facts, instructions or decisions; those live in `AGENTS.md`, `docs/`, `specs/` and `docs/adr/`. The name `GLOSSARY.md` is the one public domain-modeling skills look for (ADR 0004).

## Files

**Archive**:
A `.zap<NN>` file: a ZIP of a project folder, which TiaTools reads without unpacking it to disk.
_Avoid_: backup, zip file

**Project**:
The folder TIA Portal opens, by its `.ap<NN>` file; also loosely what an archive holds.
_Avoid_: solution, workspace

**Global library**:
An `.al<NN>` folder of master copies and types shared between projects.
_Avoid_: library archive

**Database**:
`System/PEData.plf`, the one file of a project or library that holds the program; what `docs/FORMAT.md` describes.
_Avoid_: PLF file, store

**Frame**:
One stored record in the database, followed by its hash; the last frame of an object is its current state.
_Avoid_: record, entry, page

**Object**:
One thing in the database, with a type id, an id and attributes, linked to others by relations.
_Avoid_: entity, node

## Program

**PLC**:
One controller in a project, with its own blocks and tag tables.
_Avoid_: CPU, device, station

**Block**:
A unit of the PLC program: an OB, FB, FC, DB or UDT, its kind.
_Avoid_: routine, POU, module

**Event class**:
What starts an OB: the program cycle, startup, a cyclic interrupt, an error and so on. TIA stores it as the OB's subtype.
_Avoid_: OB type, secondary type

**Program cycle OB**:
An OB whose event class is the program cycle: where the program's cycle starts, OB 1 `Main` in most programs. The viewer marks it in the tree.
_Avoid_: main OB, entry point, OB1 (as a name for the class)

**Tag table**:
A table of a PLC's tags and constants with their types, addresses and values.
_Avoid_: symbol table, variable list

**Master copy**:
A copy of blocks kept in a project or global library for reuse; it can hold a whole other program, so one block name can occur several times for one PLC.
_Avoid_: template, library block

**Environment**:
Where a block lives: `ProjectData` for the project itself, or the master copy that holds it.
_Avoid_: scope, context

**Stub**:
A block object with no number, source or interface that refers to a block held elsewhere; left out when listing blocks.
_Avoid_: placeholder, dummy block

**External access**:
What an outside client (HMI, OPC UA, Web API) may do with a data block member or a PLC tag: read and write, read only, or nothing. TIA's settings `ExternalAccessible`, `ExternalWritable` and `ExternalVisible` taken together.
_Avoid_: exposed, public, visibility

**Node list**:
The nodes of a PLC's standard SIMATIC OPC UA server interface, with their node ids, derived from the program by the rules of TIA's node set export: `tiadump opcua --nodes`, and the viewer's Nodes tab. Written as a node set file by `tiadump opcua --nodeset` and Export node set.
_Avoid_: address space dump, tag list

**OPC UA report**:
What `tiadump opcua` writes per PLC: its OPC UA server's settings, roles and server interfaces, and the external access of its data blocks and tags.
_Avoid_: OPC UA export, address space dump

**Network**:
One unit of a LAD, FBD or STL block's logic, with its own number, title and comment; TIA stores each as a compile unit, and an SCL block has one.
_Avoid_: rung, segment, compile unit (outside the format page)

**Project language**:
A language the project keeps its texts in, such as de-DE; every title, comment, alarm text and text list entry has a text per project language, which may be empty.
_Avoid_: culture (outside the format page), locale, translation

**Editing language**:
The project language a person types texts in, and the one TIA's generated source and TiaTools write by default.
_Avoid_: default language, main language, UI language

**Reference language**:
The project language TIA shows beside the editing language for translating; the reader falls back to it when a text is empty in the language written. Not to be confused with a reference project.
_Avoid_: source language, base language

**Program alarm**:
An alarm a PLC program raises by calling `Program_Alarm` on an instance in an FB: its alarm class, priority and texts are configured, not coded. The FB declares it once, as the alarm type; each instance data block of the FB holds an instance alarm, whose texts and class are its own where set, else its type's.
_Avoid_: message, PLC alarm (as a name for this kind), event

**Associated value**:
A value a program alarm passes with its signal (`SD_1` to `SD_10`), which its texts show through a placeholder.
_Avoid_: process value, parameter, accompanying value

**Alarm class**:
A project-wide class of alarms, such as Acknowledgement, that says among other things whether an alarm must be acknowledged.
_Avoid_: alarm type, category

**Text list**:
A PLC's table of texts by value or range of values, in every project language, from which an alarm text can take a text.
_Avoid_: enum, lookup table, string list

**Closed**:
What a know-how protected block or an encrypted value is to TiaTools: the archive holds only its ciphertext, so TiaTools reports it as encrypted and renders nothing more. Decryption is a separate capability it does not have (ADR 0011).
_Avoid_: locked, hidden, unreadable

## What TiaTools makes

**Dump**:
The files `tiadump dump` and the viewer's export write: one text file per block and tag table, and an `index.tsv`.
_Avoid_: extract, decompile, backup

**Generated source**:
The text TIA Portal writes with *Generate source from blocks*, which a dump matches for SCL blocks, data blocks and UDTs.
_Avoid_: export, SimaticML (which is TIA's XML export, a different thing)

**Texts table**:
The table a dump writes per PLC of every title, comment, alarm text and text list entry, one column per project language, as stored and with no fallback (spec 0014).
_Avoid_: translation table, language export

**Statement form**:
The text TiaTools writes for a LAD or FBD network or a GRAPH sequence, which TIA has no text for: its logic as SCL-like assignments and calls, a form of our own (ADR 0013).
_Avoid_: decompiled, SCL translation, pseudo code

**Comparison**:
`tiadump compare` of a dump against TIA's generated source, block by block.
_Avoid_: diff, test run

**Sample**:
A real archive the reader was checked on, named only by a letter (A, B) in this repository.
_Avoid_: the project's or plant's own name

**Demo project**:
The invented filling line shown in the README's pictures, the reference project `tests/reference/FillingLine`; with the other reference projects, the only projects whose content may appear in this repository.
_Avoid_: example project, test project

**Reference project**:
A small project TIA Portal built from invented sources, committed with its archive and the generated source TIA wrote for it, so the tests check the reader against TIA without TIA at hand (spec 0008).
_Avoid_: test project, fixture, golden file

## Repository

**Spec**:
The file in `specs/` that states what a feature must do: user scenarios, numbered requirements, success criteria. The source the code is built from.
_Avoid_: requirements doc, story, ticket

**Plan**:
The `.plan.md` file next to a spec that states how the feature is built.
_Avoid_: design, technical spec

**Bundle**:
The `docs/` folder as an Open Knowledge Format bundle: every page with frontmatter, an index, a log.
_Avoid_: wiki, knowledge base

**Router**:
A `CLAUDE.md` whose only content is `@AGENTS.md`.
_Avoid_: stub, link file
