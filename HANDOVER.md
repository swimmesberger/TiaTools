# Handover: OPC UA support, to be checked with TIA Openness

Written 2026-11-20 at the end of a session on a Mac without TIA Portal. Continue on the Windows PC with TIA Portal V21 and Openness. This branch, `handover/opcua-settings`, is never merged: `AGENTS.md` keeps session handoffs out of tracked files, so it lives apart from the feature branch. Delete it when done.

This repository is public. Nothing here or in what you add may name a real project, PLC, tag or value; the archives are called samples C, D and E, as in `docs/FORMAT.md`. Their names and the per-archive findings are in the session notes on the Mac (`tiatools-opcua-report.md` in the side-session notes folder), or run `tiadump opcua <archive>` on the PC.

## Where things stand

- Pull request swimmesberger/TiaTools#1, branch `feat/opcua-settings`, three signed commits on top of `1384976`:
  - `65cc991` feat(tiadump): `tiadump opcua` (Markdown, `--json`, `--plc`), expando reads, the serialized roles reader, secrets withheld in `inspect`. Spec 0004.
  - `c5fa7de` feat(viewer): an "OPC UA" entry first under each PLC. Spec 0005.
  - `dd519f9` feat(viewer): the structured view in TIA Portal's layout (Server, Server interfaces and roles, Data blocks, Tags). Spec 0005, approved twice, now `stable`.
- PR pictures: branch `pr-assets/opcua-viewer` (never merged; delete after the PR).
- `make check` passed on the Mac at `dd519f9` (127 library tests, 25 viewer tests, 44 script tests, repository check).
- An earlier push leaked sample details into the public branch; it was rewritten (`6aa1e57` replaced by `65cc991`). `6aa1e57` can still be opened on GitHub by its hash until GitHub Support purges it; that request is the owner's to make.

## Not committed yet: `wip/opc-ua-nodes-draft.patch`

Apply on `feat/opcua-settings` with `git apply wip/opc-ua-nodes-draft.patch` (from a checkout of this branch, or download the file). It holds:

- `specs/0006-opc-ua-nodes.md`, **draft, not approved**: the node list (node ids `ns=3;s="Block"."Member"`, browse path, SIMATIC and OPC UA type, access) in the viewer (a Nodes tab, Copy node id, Export node set) and in `tiadump opcua --nodes` / `--nodeset`, with a UANodeSet export like TIA's *OPC UA › Server › Export*. Nothing of it is built.
- The research note `docs/research/2026-11-20-tia-opc-ua-server-settings.md` extended with the address space and export (namespace, node id form, browse tree, data type table, the export's content), and its `docs/index.md` entry. It needs a log entry when committed (`python3 tools/log_add.py ...`).

## What to check with TIA and Openness

These are the readings marked inferred (`[I]`/`[?]`) in `docs/FORMAT.md` section 18 and open question 6. Each check either confirms a mark, then turn it into `[S]`, or changes the reader, its tests and the format page in one commit.

1. **Security policy names.** Open sample C in TIA, CPU properties › OPC UA › Server › Security › Secure channel, for each PLC. Compare the ticked policies with what `tiadump opcua` shows for the keys `EnableSecurityPolicies_None`, `_256_S`, `_256_SE`, `_256SHA_S`, `_256SHA_SE`, `_256RSA_PSS_SE`, and with the names `TiaOpcUaSecurityPolicy.Name` gives (`src/TiaFormat/Program/TiaOpcUa.cs`). Through Openness, the OPC UA device item's attributes (`DeviceItem.GetAttributeInfos()` on the item whose `ConfigObjectTypeName` holds `\OPC_UA\`) may name them directly.
2. **Tag access.** In sample C, `tiadump opcua` lists tags with `HmiStructureTypeRelevant` clear and `HmiVisible` set (shown as not accessible but visible). Check those tags' columns *Accessible from HMI/OPC UA/Web API*, *Writable…*, *Visible in HMI engineering* in TIA's tag table, or read `ExternalAccessible`, `ExternalWritable`, `ExternalVisible` of the `PlcTag` objects through Openness, and compare with the stored flags (`TiaProgram.ReadTagTables`). This decides whether those tags are reachable over OPC UA at all.
3. **Port, guest access, user name and password authentication, minimum sampling interval, standard SIMATIC server interface.** No sample stores them. In a copy of a project (never write to an archive), change each in TIA, save, archive, and run `tiadump inspect <archive> <id of the OPC UA device item>`: the new `~` keys are their names. Add them to `TiaOpcUa.ReadServer` and to the view's Options and User authentication sections, with a test on a built file.
4. **The node list (spec 0006), before building it.** On a PLC with the server on, use TIA's *OPC UA › Server › Export* to write the node set XML, or browse the running server with a client such as UaExpert. Note the folder names under the PLC's object (the research note confirms only *DataBlocksGlobal* and *Counters*), instance data blocks, structs, arrays, tags by address area, and the node ids of the PLC object and the folders. Correct the draft spec, then build it. Keep the export file itself out of the repository: it holds the project's names.
5. **Data block and member access** (`MFlags` rule of section 11): spot-check a closed and a read-only member in TIA's data block editor.

Openness setup and the export tool are in `docs/comparing-with-tia.md` (`tools/TiaOpennessExport`, Windows, TIA V21, the `Siemens TIA Openness` user group).

## Useful bits

- Run everything with the repository's commands in `AGENTS.md`; `make check` before committing. On the Mac `DOTNET_ROLL_FORWARD=Major` was needed; probably not on the PC.
- `scratch/opcua-shots/` here is the headless screenshot tool used for the PR pictures, from the invented test project (`TiaOpcUaFixture`). Copy it to `.scratch/opcua-shots/` (git ignores `.scratch`) and run `dotnet run --project .scratch/opcua-shots -c Release -- <output folder>`.
- Specs 0004, 0005 and 0006 follow `specs/README.md`; a spec needs the owner's approval (status `stable`) before code.
