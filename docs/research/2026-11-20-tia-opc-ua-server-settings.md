---
type: Research note
title: "How TIA Portal shows an S7-1500's OPC UA server settings and address space"
description: "The pages, labels and defaults TIA Portal uses for a CPU's OPC UA server, where it shows OPC UA access of data blocks and tags, and how the server names its nodes and exports them."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-20T12:00:00Z }
status: draft
sources:
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=166
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=176
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=180
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=182
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=188
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=189
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=194
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=198
  - https://epics-modules.github.io/opcua/how-to/plc_configuration.html
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=136
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=137
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=143
  - https://www.manualslib.com/manual/1971486/Siemens-Simatic-S7-1500.html?page=175
  - https://support.industry.siemens.com/cs/attachments/109737901/109737901_OPC_UA_Client_S7-1500_DOKU_V1_5_en.pdf
---
# How TIA Portal shows an S7-1500's OPC UA server settings

The question: how TIA Portal lays out a CPU's OPC UA server, so that TIA Viewer can show what `tiadump opcua` reads in the shape an engineer knows. Main source: Siemens' S7-1500 function manual on OPC UA communication, edition 11/2019 (A5E03735815-AH), read through ManualsLib because Siemens' own download link answered 404. Printed page numbers below; ManualsLib numbers are one higher. The manual names no TIA Portal version, and later versions added settings (the AES policy below), so treat labels as a guide, not as TIA's current text.

## The pages

In the CPU's properties, under *OPC UA*:

| Page | What it holds | Manual |
|---|---|---|
| General | "Enable additional user administration via the security settings of the project" | p. 194 |
| Server › General | Activating the server, after confirming security notes; the server addresses, one per interface | p. 175, 177 |
| Server › Options | "TCP port for OPC UA" (4840, range 1024 to 49151); "Enable standard SIMATIC server interface"; subscriptions: "Minimum publishing interval", "Minimum sampling interval", "Maximum number of monitored elements" | p. 179-183 |
| Server › Security › Secure channel | The server certificate; security policies, one check box per line; "Trusted clients"; "Automatically accept all client certificates during runtime" | p. 184-189 |
| Server › Security › User authentication | "Enable guest authentication"; "Enable user name and password authentication" with a user list | p. 193-194 |
| Server › Diagnostics | Diagnostic settings of the server | p. 197 |
| Server › Export | Export of the server's XML description | p. 174 |
| Runtime licenses › OPC UA | The license the server needs | p. 175, 198 |

The EPICS guide shows the same tree as *General › OPC UA › Server › General / Options / Security* and *Runtime licenses*, and names the check box "Activate OPC UA server".

## Defaults

- The server is off: "not enabled for reasons of security" (p. 175).
- Port 4840; the standard SIMATIC server interface is on (p. 179).
- In this edition every security policy is on by default (p. 188). TIA lists them in this order: no security, Basic128Rsa15 sign, sign and encrypt, Basic256 sign, sign and encrypt, Basic256Sha256 sign, sign and encrypt; Basic256Sha256 is the recommended one (p. 188-189). The key tables of the samples name no Basic128Rsa15 key but one for a policy with RSA-PSS (`256RSA_PSS_SE`), which this edition does not mention: later TIA versions changed the list.
- The manual recommends user authentication over guest access (p. 193) and switching automatic acceptance of client certificates off after commissioning (p. 187).

## Data blocks and tags

- A data block's properties, area *Attributes*, hold the check box "DB accessible from OPC UA"; data blocks are open by default, and the program blocks overview has a column "Data block accessible via OPC UA" (p. 167-168).
- A PLC tag or data block member has the check box "Accessible from HMI/OPC UA", on by default (p. 165). TIA's tag table and block editors show it as a column. The writable and visible settings that a TIA source file writes as `ExternalWritable` and `ExternalVisible` are columns there too, but this edition of the manual does not name them, so their labels here are not confirmed.

## The address space and its export

What a client of the standard SIMATIC server interface sees, from the same manual and Siemens' application example "OPC UA .NET Client for the SIMATIC S7-1500 OPC UA Server" (entry 109737901, V1.5, 09/2022):

- All CPU tags and methods are in the namespace `http://www.siemens.com/simatic-s7-opcua`, index 3 by default, but the index can change, so a client asks for it (manual p. 135; example p. 55).
- A node's identifier is the tag's name in quotation marks, with its components joined by dots: `ns=3;s="DataStatic"."myBool"` (manual p. 136-137; example p. 55, figure 3-2).
- The browse tree in the example (p. 41): *Objects › Server*, *DeviceSet*, and the PLC's object named after the PLC (`PLC_1`) with device properties and the folders *Counters*, *DataBlocksGlobal* and further ones cut off in the picture; a data block is an object in *DataBlocksGlobal*, its members are variables below it. The member `myBool` there has browse name `3:myBool`, data type Boolean, access level 3 (read and write) and minimum sampling interval -1.
- A structure or UDT member is a node of its own, read as a whole or by its members (example p. 49).
- Data types map as in the manual's table 9-1 (p. 142-143): `Bool` to Boolean, `Byte` to Byte, `Word` to UInt16, `DWord` to UInt32, `LWord` to UInt64, `SInt` to SByte, `Int` to Int16, `DInt` to Int32, `LInt` to Int64, `USInt` to Byte, `UInt` to UInt16, `UDInt` to UInt32, `ULInt` to UInt64, `Real` to Float, `LReal` to Double, `S5Time` to UInt16, `Time` to Int32, `LTime` to Int64, `Date` to UInt16, `TOD` to UInt32, `LTOD` to UInt64, `DT` to Byte[8], `DTL` to DateTime.
- A data block closed to OPC UA has no nodes, its instance data blocks included; a data block whose members are all closed is not in the address space either (p. 168).
- *OPC UA › Server › Export* writes the server's address space as an XML "nodeset file", "structured according to the OPC UA specification", with all tags and methods enabled for OPC UA, for configuring a client offline; each tag's `Value`, `DataType`, `AccessLevel` and `MinimumSamplingInterval` can be set in it, and the option "Export all array elements as separate nodes" adds a node per array element (p. 174-175).

Not found in the sources: the names of the folders cut off in the picture (presumably for instance data blocks, inputs, outputs, memory and timers), the node ids of the PLC object and the folders, and how array elements and multi-instances are written.

## What it means for TiaTools

- The stored key suffixes of section 18 of the [format](../FORMAT.md) line up with this list: `None`, `128RSA_S`/`128RSA_SE` for Basic128Rsa15, `256_S`/`256_SE` for Basic256, `256SHA_S`/`256SHA_SE` for Basic256Sha256, and `256RSA_PSS_SE` for the later policy with RSA-PSS. Section 18 has since checked them, and the keys of port, guest access and user authentication, in TIA V21 through Openness, with TIA V21's defaults, which differ from this edition's: there only No security and Basic256Sha256 are on by default.
- A view that follows TIA's pages (General, Options, Security, User authentication) and its access columns for data blocks and tags will read familiar. A setting that is not stored belongs on its page as "not stored" with TIA's default, not left out.
