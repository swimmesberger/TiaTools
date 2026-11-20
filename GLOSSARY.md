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

**Closed**:
What a know-how protected block or an encrypted value is to TiaTools: reported, never opened.
_Avoid_: locked, hidden, unreadable

## What TiaTools makes

**Dump**:
The files `tiadump dump` and the viewer's export write: one text file per block and tag table, and an `index.tsv`.
_Avoid_: extract, decompile, backup

**Generated source**:
The text TIA Portal writes with *Generate source from blocks*, which a dump matches for SCL blocks, data blocks and UDTs.
_Avoid_: export, SimaticML (which is TIA's XML export, a different thing)

**Comparison**:
`tiadump compare` of a dump against TIA's generated source, block by block.
_Avoid_: diff, test run

**Sample**:
A real archive the reader was checked on, named only by a letter (A, B) in this repository.
_Avoid_: the project's or plant's own name

**Demo project**:
The invented filling line in `demo/`, the only project whose content may appear in this repository.
_Avoid_: example project, test project

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
