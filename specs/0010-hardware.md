---
type: Specification
title: "Spec 0010: hardware and subnets, read and checked against TIA Portal"
description: "The reader lists a project's devices with their racks and modules, order numbers, firmware and I/O addresses, its PROFINET and PROFIBUS subnets and IO systems, and each interface's addresses, and a reference project built by TIA Portal checks all of it against what TIA reports through Openness."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T16:00:00Z }
status: stable
---
# Spec 0010: hardware and subnets, read and checked against TIA Portal

## Intent

A PLC program is only half of what a project says about a machine: which CPU runs it, which modules sit in its racks and at which I/O addresses, which IO devices it reaches over which subnet, and at which IP addresses and PROFINET device names. Today the reader lists no devices at all (`docs/FORMAT.md`, open question 5), so someone reading a project without TIA Portal, a person or a coding agent, cannot tell where `%I10.0` comes from or which remote station a module is in. `tiadump devices`, and an entry in the viewer, list every device of a project with its racks and modules, their order numbers, firmware and I/O addresses, the project's subnets and IO systems, which IO devices belong to which IO controller, and each interface's addresses, as text and as JSON. A new reference project, invented and built by TIA Portal, holds a CPU with central modules, IO devices on PROFINET and PROFIBUS and two IO controllers on one subnet, and every test run checks what the reader lists against what TIA reports for the same project through Openness.

## User scenarios

### List the devices of a project (P1)

- Given an archive, when `tiadump devices` runs on it, then it lists every device of the project by name, with the device group it is in, and under each device its racks and the modules in them by slot, with each module's name, order number and firmware version.
- Given a device item that is not in a slot, such as a CPU's interface or a port, then it is listed under the module it belongs to.
- Given a module with inputs or outputs, then it is listed with each of its I/O address ranges: inputs or outputs, the first and the last byte.
- Given an archive without devices, such as a global library, then the command says so and exits without error.
- Given `--json`, then the same content is written as JSON, one object per device, subnet and IO system.

### See how the devices are connected (P1)

- Given a project with subnets, then the command lists each subnet by name and kind, PROFINET/Industrial Ethernet or PROFIBUS, with the interfaces connected to it.
- Given an IO system, then the command lists it by name and number, with the subnet it is on, the IO controller that owns it, and each IO device assigned to it.
- Given a device whose interface is connected to a subnet but not assigned to an IO system, then it is listed with its subnet and no IO system.
- Given two IO controllers on one subnet, each with an IO system of its own, then each IO device is listed under the IO system it is assigned to, and only there.

### See an interface's addresses (P1)

- Given a PROFINET or Industrial Ethernet interface, then it is listed with its IP address, its subnet mask, its PROFINET device name, and whether TIA generates that name from the device's name.
- Given a PROFIBUS interface, then it is listed with its PROFIBUS address.
- Given an interface whose IP address is set elsewhere than in the project, such as on the device, then it is listed as such, with no address.

### Find what the program calls a module (P2)

- Given a device item that has a hardware identifier, then it is listed with that identifier and the name of the system constant through which the program refers to it, as the PLC's tag table lists that constant.

### See the hardware in the viewer (P2)

- Given an archive whose project has devices, when it is opened in the viewer, then the project's node in the tree holds an entry "Devices & networks" first.
- Given the entry is chosen, then the view shows each device with its device items nested as `tiadump devices` lists them, with the same columns, then the subnets and the IO systems.
- Given a device item with a system constant, when that constant is opened, then the viewer shows it in its PLC's tag table, and Back returns to the view.
- Given the view is shown, when Copy is chosen, then the text `tiadump devices` writes is copied.
- Given text that occurs only in the hardware, when all source is searched, or the project is exported, then the hardware is neither found nor written.
- Given an archive without devices, then the tree holds no such entry.

### The tests check the hardware against TIA Portal (P1)

- Given the hardware reference project, when the library's tests run on a machine without TIA Portal, then every device, device item, subnet, IO system and interface address the reader lists is compared with what TIA reported for the same project through Openness when it built it.
- Given a difference, when the tests run, then they fail and name the reference project, the device, the device item and the attribute, with both values.
- Given something TIA reported that the reader does not list, or the other way round, then the tests fail and name it.
- Given the reference project's description of its hardware, when the maintainer builds it again, then TIA builds the devices, modules, subnets and IO systems it describes, archives the project and writes what it reports about them, and a device or module TIA refuses is reported with TIA's error and the committed files stay unchanged.

### The reference project covers the parts of a hardware configuration (P2)

- Given the hardware reference project, then it holds: a CPU with modules of input, output, analog input and analog output in its rack, one with an I/O address set by hand; an IO device on PROFINET with modules, one with a PROFINET device name set by hand and one with a generated name; an IO device on PROFIBUS with modules; a second PLC on the same PROFINET subnet with an IO system and an IO device of its own; a device connected to a subnet but assigned to no IO system; a second subnet; and a device inside a device group.

## Requirements

- FR-001: `tiadump devices <archive>` MUST list every device of the project with its name and the path of the device group it is in.
- FR-002: For each device it MUST list the device items TIA shows in its device view, nested as TIA nests them, each with its name and slot, and with the order number and firmware version where TIA shows them.
- FR-003: For each device item with inputs or outputs it MUST list each I/O address range with its kind, inputs or outputs, its first byte and its length, as the project stores them.
- FR-004: It MUST list every subnet with its name and kind, and the interfaces connected to it.
- FR-005: It MUST list every IO system with its name, its number, its subnet, the interface of the IO controller that owns it, and each IO device assigned to it.
- FR-006: For each PROFINET or Industrial Ethernet interface it MUST list the IP address and subnet mask, the PROFINET device name and whether that name is generated, or that the IP address is set outside the project.
- FR-007: For each PROFIBUS interface it MUST list the PROFIBUS address.
- FR-008: `--json` MUST write the same content as the text, as one JSON document.
- FR-009: The command SHOULD list each device item's hardware identifier and the name of the system constant that refers to it.
- FR-010: The command MUST NOT show passwords, keys, certificates or protection settings stored with a device, nor anything encrypted; a device item that holds any of them is listed without them.
- FR-011: An archive with no devices MUST give an empty list and exit 0.
- FR-012: A reference project MUST hold invented hardware covering every part listed in the sixth user story, built by TIA Portal V21 from a description committed beside it.
- FR-013: Building a reference project MUST also write, for every device, device item, subnet, IO system and interface, the attributes and I/O addresses TIA reports for it through Openness, beside the generated source.
- FR-014: Building MUST report every device, module, subnet or IO system TIA refuses, with TIA's error, in one run, and MUST leave the committed files unchanged when there is any.
- FR-015: The library's tests MUST compare what the reader lists for each reference project that holds TIA's report of its hardware with that report, and fail on a difference, naming the reference project, the device, the device item, the attribute and both values; something listed by one side only MUST fail too.
- FR-016: `docs/FORMAT.md` MUST describe where the hardware is stored, with each claim marked as the format page marks claims.
- FR-017: The viewer MUST show an entry "Devices & networks" first under the project's node in its tree when the project has devices, and none when it has none.
- FR-018: Choosing that entry MUST show a read-only view of the same content `tiadump devices` lists: the devices with their device items nested, then the subnets and IO systems, read when first shown, not when the archive is opened.
- FR-019: Opening a device item's system constant in that view SHOULD show the constant in its PLC's tag table, as following a name in the source does.
- FR-020: Copy in that view MUST copy the text `tiadump devices` writes.
- FR-021: The hardware MUST NOT count as an item of the program in the viewer: it MUST NOT be searched, referenced, counted in the status line, or exported, and its entry MUST NOT offer an export.

## Success criteria

- SC-001: For the hardware reference project, every device, module, order number, firmware version, I/O address, subnet, IO system, IP address and PROFINET device name TIA reports agrees with what `tiadump devices` lists.
- SC-002: Someone with only an archive, at the command line or in the viewer, can tell, for any I/O address the program uses, which module in which device it belongs to.
- SC-003: After TIA Portal is no longer available, a change that breaks how the reader reads any covered part of the hardware fails the tests in CI.

## Out of scope

- Module parameters: channel settings, diagnostics, measuring ranges, cycle times, protection levels, web server and other CPU settings.
- Port interconnections and topology, isochronous mode, media redundancy, shared devices and I-devices beyond what an IO system lists.
- HMI devices beyond their name, order number and interfaces as for any device; their screens and tags belong to the HMI spec.
- Fail-safe settings of modules, such as F-addresses and F-parameters; they belong to the safety spec.
- Technology objects, connections between PLCs (S7 and open user communication), and the cross references between tags and modules.
- Devices in master copies or global libraries.
- Writing anything back, or exporting the hardware in a form TIA can import, such as AutomationML.

## Decisions

- [ADR 0012](../docs/adr/0012-reference-projects-are-committed.md): reference projects are committed with what TIA generated for them; the hardware reference project adds TIA's report of its hardware to that.
