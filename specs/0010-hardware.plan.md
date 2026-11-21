---
type: Plan
title: "Plan 0010: hardware and subnets"
description: "How spec 0010 is built: where the hardware is stored, the reference project Hardware and its description, TIA's report of the hardware, the library's model, tiadump devices, the viewer's entry, and the tests."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T16:00:00Z }
status: stable
---
# Plan 0010: hardware and subnets

## Where the hardware is stored

Read with a scratch probe over every object of `FillingLine.zap21` and then of the new reference project; `docs/FORMAT.md` section 19 has the result with its marks. In short: devices are `DeviceData`, device items the objects implementing `IDeviceItemData`, nested by `Parent`; an interface's addresses are a `NodeData`'s dictionary expando values; I/O addresses are `AddressItem` structures in dictionary expando values `AM_<n>_AddrData_<m>`; subnets are `SubnetData`, IO systems `MastersystemData`; hardware identifiers are the values of system constants reached by `ConstantTags`. Which stored device items TIA lists comes from its catalog, not the file, so the reader follows a rule on subtypes, checked on the reference project.

## Reference project `Hardware`

`tests/reference/Hardware/` as spec 0008 lays out, plus `sources/hardware.tsv`, the invented hardware, one tab-separated row per thing, applied in file order:

| Row | Fields |
|---|---|
| `subnet` | name, kind (`Ethernet`, `Profibus`) |
| `device` | device name, order number, firmware (empty: the newest in the catalog), device group path (`/`-separated, may be empty); the item `CreateWithItem` creates, the CPU or interface module, takes the device's name |
| `module` | device, slot, order number, firmware, name, start addresses (`I=10`, `Q=20`) |
| `interface` | device, interface name, subnet, then for Ethernet IP address, subnet mask, PROFINET device name or `auto`; for PROFIBUS the address alone |
| `ipelsewhere` | device, interface: the IP address is set at the device; TIA allows it on an IO controller's interface only |
| `iosystem` | controller device, interface, IO system name, number |
| `iodevice` | device, interface, IO system name |

The content (FR-012): PLC `Controller`, a CPU 1516-3 PN/DP, with DI 32, DQ 32, AI 8 and AQ 4 modules in its rack, the DI at I 10 by hand; X1 on `PN/IE_1` owning `Line IO system` (100) with the ET 200SP IO devices `Remote_PN_1` (device name set by hand, DI 8, DQ 8, AI 4) and `Remote_PN_2` (generated name); X2 on `PN/IE_2`, its IP address set at the device; X3 on `PROFIBUS_1` owning `Line DP system` (1) with the ET 200SP DP device `Remote_DP` (DI 8, DQ 8); PLC `Partner`, a CPU 1511-1 PN, on `PN/IE_1` with `Partner IO system` (101) and its IO device `Partner_IO`; `Spare_PN`, an ET 200SP on `PN/IE_1` in no IO system, in the device group `Line/Cell`. Each PLC has a program of one OB that reads an input and sets an output, so spec 0008's block comparison has a block to compare.

Order numbers come from TIA V21's catalog through `HardwareCatalog.Find`: the given firmware's entry, or the newest; the build prints the type identifier it chose. `TiaOpennessExport catalog <text>...` lists the entries that match, which found the IM 155-6 DP HF's order number.

## Building and TIA's report (FR-012 to FR-014)

`tools/TiaOpennessExport`, in `Hardware.cs`:

- `HardwareBuilder` applies `sources/hardware.tsv` when it is there: subnets through `project.Subnets.Create`, devices through `Devices.CreateWithItem`, in a group made with `DeviceGroups.Create`, modules through `PlugNew` on the device item that can take them, start addresses through `Address.StartAddress`, interfaces through `NetworkInterface.Nodes[0]` (`ConnectToSubnet`, attributes `Address`, `SubnetMask`, `PnDeviceNameAutoGeneration`, `PnDeviceName`, `IpProtocolSelection`), IO systems through `IoController.CreateIoSystem` and the attribute `Number`, IO devices through `IoConnector.ConnectToIoSystem`. Each refused row is printed with TIA's message and counted as a refused source, so the committed files stay as they are. Each PLC folder then names a CPU the description created. Without `hardware.tsv` a project is built as before: one CPU per PLC folder.
- `HardwareReport` writes `generated/hardware.tsv` after the archive, for every project the command builds: rows of `kind`, `path`, `attribute`, `value`, where `path` joins a device's and its device items' names with ` » `. It writes every attribute Openness can read of each device, device item, address, interface node, subnet and IO system, their hardware identifiers, each interface's subnet and the IO system it controls or is assigned to, and each IO system's subnet and IO devices. Attributes named like a password, key, hash or certificate, the author's name and the time a module was added are left out.

The other reference projects get a `hardware.tsv` the next time they are built; until then the hardware tests skip a project without one.

## Library (FR-001 to FR-011)

`src/TiaFormat/Hardware/`, a namespace beside `Program`:

- `TiaHardware.Read(TiaProgram)` returns `TiaHardwareReport`: `Devices`, `Subnets`, `IoSystems`. `TiaDevice` (name, group, type name, its own system constants, items), `TiaDeviceItem` (name, path, slot, order number, firmware, type name or label, system constants, I/O addresses, interface addresses, items), `TiaIoAddress` (kind, start byte, length in bits; `I 10..13` as text), `TiaInterfaceAddress`, `TiaSubnet`, `TiaIoSystem` (with its system constants), `TiaSystemConstant` (name, hardware identifier, type, PLC). `TiaHardware.HasDevices` answers without reading the hardware.
- Only devices of the project itself. Items by slot, a rack first, then by name.
- It reads named attributes only, so nothing else stored on the same objects is read (FR-010).
- `TiaDatabase.ReadExpandoStructure` gives a structure-valued expando value's bytes, and `TiaSchema.FindValueType` a value type by id, for it.
- `TiaHardwareWriter.WriteText` and `WriteJson`.

## Command

`tiadump devices <archive> [--json]` in `TiaDumpCommands`. The text lists each device with its type, group and own constants, its device items indented by depth as `slot name  type  order  firmware  addresses  constants`, an interface's addresses on a line below it, then the subnets with their interfaces and the IO systems with number, subnet, constants, IO controller and IO devices. The writer's tests pin it.

## Tests

- `TiaHardwareReferenceTest`: per reference project with `generated/hardware.tsv`, `HardwareReports` flattens the reader's report and TIA's into rows of one vocabulary and compares them per path and attribute, naming project, path, attribute and both values, or the side that lacks one (FR-015): `EveryDeviceAndDeviceItemMatchesTiasReport`, `EveryIoAddressMatchesTiasReport`, `EveryHardwareIdentifierMatchesTiasReport`, `EveryInterfaceLabelTheArchiveHoldsMatchesTiasReport`, `EveryInterfaceAddressMatchesTiasReport`, `EverySubnetAndIoSystemMatchesTiasReport`, and on the Hardware project `EveryIoSystemListsTheIoDevicesTiaAssigned`, `AnIoDeviceIsListedOnlyUnderItsOwnIoSystem`, `ADeviceOnASubnetWithoutIoSystemHasNone`, `AnIpAddressSetAtTheDeviceIsListedWithoutAnAddress`, `AModuleShowsItsIoAddressAsTiaWritesIt`, and the failure message on made-up rows. Port labels are compared nowhere, since TIA takes them from its catalog; hardware identifiers only where a system constant holds them, per device.
- `TiaHardwareWriterTest`: `TheTextListsEveryDeviceSubnetAndIoSystem`, `TheTextShowsAModuleWithItsOrderNumberFirmwareAndIoAddress`, `TheTextShowsAnInterfacesAddresses`, `TheTextShowsWhichIoDevicesBelongToWhichIoController`, `TheJsonHoldsWhatTheTextHolds`, `NoSecretIsListed`, `AnArchiveWithoutDevicesGivesAnEmptyList` (a `TiaFileBuilder` file).
- `TiaExpandoTest.AStructureValueIsReadFromItsSizeOnWithItsElementsInline` for the new storage read.

## Viewer (FR-017 to FR-021)

As the OPC UA entry of spec 0005 does:

- `TiaItem.HardwareReportKind` (`Devices & networks`), one per project that has devices, first under the project node; `TiaItem.IsReport` covers it, so search, references, counts and export leave it out (FR-021). It gets no switch among the kinds: it is one entry. `TiaProject.Hardware(item)` reads the hardware on first use and keeps it, and `TiaProject.Document` of the entry is the text `tiadump devices` writes, for Copy (FR-020). `TiaProject.Resolve(plc, name)` finds a system constant in its PLC's tag table.
- `src/TiaViewer/DevicesView.cs`, a panel in the editor's place: tabs Devices (a tree of devices and device items with the columns of the text and their interface addresses), Subnets and IO systems. A system constant is a link that opens it in its PLC's tag table (FR-019). The badge of the entry reads `HW`.
- `TiaProjectHardwareTest`: `TheProjectNodeHoldsDevicesAndNetworksFirst`, `TheEntryShowsTheHardware_ReadWhenFirstShown`, `CopyGivesTheTextTiadumpDevicesWrites`, `ASystemConstantOpensInItsPlcsTagTable`, `TheHardwareIsNeitherSearchedNorCountedNorExported`, `AnArchiveWithoutDevicesHasNoDevicesEntry`. The panel is checked by running the viewer; `tools/ReadmePictures` takes a picture of it from the demo project.

## Docs

- `docs/FORMAT.md`: section 19 Hardware, the reference project in section 17, open question 5 narrowed to what the reference project does not cover.
- `GLOSSARY.md`: **Device**, **Device item**, **Module**, **Interface**, **Subnet** (avoid "network", which is a block's), **IO system**, **IO controller**, **IO device**, **I/O address**, **Hardware identifier**.
- `README.md`: the command, the viewer's entry and its picture. `.agents/skills/read-tia-project`: `tiadump devices`. `docs/comparing-with-tia.md`: `hardware.tsv` in the sources and in `generated/`. `docs/architecture.md`, `AGENTS.md`: the `Hardware` namespace. `docs/log.md`: an entry.
- No ADR: the description file and the report are formats of the build tool, easy to change; they rest on ADR 0012.
