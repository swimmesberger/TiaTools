---
type: Format reference
title: "The TIA Portal project database format"
description: "The file format of TIA Portal projects, archives and libraries, enough to write a read-only reader in any language, with how far real archives confirm each claim."
status: stable
---
# The TIA Portal project database format

Enough to write a read-only reader in any language: container, frames, schema, values, relations, then the PLC block layer (interface, SCL, FBD/LAD networks, identifiers), technology objects, the OPC UA configuration and the hardware. `src/TiaFormat` implements it, and `src/TiaDump` dumps an archive's program as source files with it.

Checked on four files: two V20 archives (`.zap20`, called A and B here, 58 and 89 MB), A upgraded by TIA V21 (`.zap21`, 59 MB) and one V15.1 global library (`.al15_1`, 4 MB). The OPC UA configuration (section 18) was checked on three more: two V20 archives, C and D, and one V18 archive, E. Thirteen reference projects, small archives TIA V21 built from invented sources (section 17), are checked against TIA in every test run; `Hardware` among them for the hardware (section 19), `Tables` for watch and force tables and the cross-reference list (sections 20 and 21), `Safety` for the safety program (section 22), `TechnologyObjects` for technology objects (section 23), `MoreLanguages` for GRAPH step times, more STL, ProDiag and S7-300 blocks (section 13), `Hmi` for HMI devices (section 24), `Texts` for texts in several project languages, program alarms and text lists (section 25). The HMI layer was found on projects TIA V21 built before it was checked on `Hmi`. Siemens publishes no description of this format, so it can change with a TIA version.

## How to read the claims

| Mark | Meaning |
|---|---|
| **[S]** | Confirmed on the samples by a check that could have failed. Section 17 lists the numbers. |
| **[U]** | Untested: the samples never exercise it, or it was not checked. Treat it as a lead. |
| **[I]** | Inferred from the samples, with no check that could have refuted it. |
| **[?]** | Unknown. |

**The generated text of SCL blocks, data blocks and UDTs has been compared with TIA Portal,** through Openness and for every unprotected block of one project: section 17 has the result. STL has been compared the same way, and LAD, FBD and GRAPH through TIA's export of invented blocks written in the same statement form, on the reference projects (section 13). Fail-safe F-FBD and F-LAD have been compared the same way on invented blocks, the samples' only by eye. A PLC's safety program (section 22) is compared with what TIA reports through Openness. Everything else stored but not rendered has not been compared at all.

## 1. The containers

- `.zap<NN>` (`.zap20`): a ZIP of a project folder. **[S]**
- `.al<NN>` (`.al15_1`): a global library, already an unpacked folder. **[S]**

| Path | Content |
|---|---|
| `System/PEData.plf` | The database: this document. |
| `System/PEData.idx` | Index over it (section 9). Not needed to read the file; useful as a check. |
| `*.ap20`, `*.al15_1` at the top | UTF-8 XML: `Type`, `Name`, `ProjectCompatibilityVersion`/`ProjectVersion`, an icon. |
| `XRef/XRef.db` | SQLite: TIA's cross-reference list, section 21. **[S]** |
| `Vci/Vci.db` | SQLite, empty in both V20 samples. |
| `AdditionalFiles/`, `Logs/` | Plain files; `GSD` device descriptions among them. |

## 2. Conventions

All integers are little-endian. **[S]**

**varint**: base-128, low 7 bits first, top bit means another byte follows. **[S]**

**Timestamp** (`xs:dateTime`): u64 .NET ticks. Mask with `0x3FFFFFFFFFFFFFFF`; `0` means none; Unix seconds = `(ticks - 621355968000000000) / 10000000`. **[S]**

**Ordinal sort**: byte order of the name, never locale order. It decides the layout in sections 6 and 8. **[S]**

## 3. File header: 98 bytes

| Offset | Size | Field | |
|---|---|---|---|
| 0 | u16 | File type: 1 storage, 2 index, 64 composite, 128 composite index | **[S]** `.plf` 64, `.idx` 128 |
| 2 | u16 | Protocol, 0 in the samples | **[U]** |
| 4 | u32 | Id, 1 in the samples | **[U]** |
| 8 | 16 | Guid; `.plf` and `.idx` share it | **[S]** |
| 24 | 4×u32 | Product version: `2100.0.121.1` is V21, `2000.0.9501.1` is V20, `1501.0.2801.1` is V15.1 | **[S]** |
| 40 | u32 | | **[?]** 0 |
| 44 | 4×u32 | Format version: `1400.1.0.0`, V15.1 `1400.0.0.0` | **[S]** |
| 60 | 5 | Further version byte and node, 0 in the samples | **[U]** |
| 65 | 32 | SHA-256 of bytes 0 to 64 | **[S]** |
| 97 | u8 | `0xFF` | **[S]** |

Files with a product major below 1400 use other layouts: a 45 or 46 byte file header and a 27 or 28 byte frame header. **[U]**

## 4. Frames

From byte 98 to the end of the file: frame, 32 byte hash, frame, 32 byte hash, ... The walk ends exactly at end of file. **[S]**

| Offset | Size | Field | |
|---|---|---|---|
| 0 | u32 | `size` of the frame, excluding its hash | **[S]** |
| 4 | u32 | Type id | **[S]** |
| 8 | u64 | Object id | **[S]** |
| 16 | u32 | Cluster id; equals the index's cluster | **[S]** |
| 20 | u32 | Info flags, see below | **[S]** |
| 24 | u16 | States, see below | **[S]** |
| 26 | u8 | `OffsetCount`: entries in the offset table (section 7) | **[S]** |
| 27 | u8 | `MetaId`: constant in the V20 samples (5, 19); not needed to read a frame | meaning **[?]** |
| 28 | 16 | Guid | **[S]** |
| 44 | `size`-44 | Body | |
| `size` | 32 | SHA-256 of bytes 0 to `size`-1 | **[S]** |

**States**: `0x04` deleted **[S]**; `0x08` destroyed **[U]**; `0x20` a reference to an object held elsewhere (see "stub", section 10) **[S]**. The index adds `0x40` segment holder and `0x80` has a core text. **[U]**

**Info flags**: bit 0 low-filled relations **[S]**; bit 1 supports segment info, bit 2 has embedded segments, bit 3 has a core text attribute **[U]**.

**Which frame is current** **[S]**: for each `(type, id)` the last frame in the file wins, and an object whose last frame is deleted is gone. Evidence in section 17: `PEData.idx` agrees for every id. The archives are compacted, so each id appears once and nothing is deleted; the V15.1 library is a real log with up to 9 versions of an object and 199 deleted ones. So a reader cannot assume "one frame per id" even though the `.zap20` samples allow it.

Frames written after the last commit point are probably not valid. No sample has any, so this is untested. **[I]**

## 5. System objects: type ids `0x70000` to `0x7001F`

Body: u32 `n`, `n` bytes payload, `0xFF`. So `frame size = 44 + 4 + n + 1`. **[S]** The names are labels; only those with a layout here were checked.

| Type | Name | Payload |
|---|---|---|
| `0x70000` | SystemMeta | zlib-compressed schema XML (section 6). **[S]** The V15.1 file holds three, growing: 110, 477, 825 types; use the last. **[S]** |
| `0x70001` | PackageConstellationMap | |
| `0x70002` | ExpandoMappingTable | u32 count, then `object type:u32, table type:u32, table id:u64`. **[S]** |
| `0x70003`, `0x70004` | Instance, cluster allocation | |
| `0x70005`, `0x70006` | Cluster mapping, cluster collection | |
| `0x70009` | RootObjects | u32 count, then `type:u32, id:u64, varint length, name`. Entry points such as `Products`, `Packages`. **[S]** |
| `0x7000A`, `0x7000C` | Feature configuration, statistics | |
| `0x7000D` | StorageVersion | Timestamps. **[S]** |
| `0x7000E` | LocalizationTable | |
| `0x7000F` | PackageConstellation | **[U]** |
| `0x70011` | Expando | 12 bytes, u32 count, then `key:u32, varint length, name, type:u32`. Expando key table. Unlike a string (section 7.2), the length counts the name alone. **[S]** |
| `0x70013` | Segment | Up to 32 pages of a large blob (section 7.3). **[S]** |
| `0x70014` | CommitPoint | Ticks at 20; `$$COMMIT$` at 34. **[S]** |
| `0x70015` | ClosePoint | Ticks at 20; `##CLOSE##` at 34. **[S]** |
| `0x70016` | TrustAnchor | The first frame of a compacted file. |
| `0x70017` | SystemMetaList | Only in sample B. |

## 6. The schema

The `0x70000` XML, namespace `http://www.siemens.com/Automation/2004/04/ObjectFrame/Meta`. It declares `Package > Namespace > (Enumeration | Structure | Array | AttributeSet | ObjectType)`, each with a numeric `id` and a `name`; the full name is `<namespace>.<name>`. A `type` or `ref` holding neither `.` nor `:` is relative to its namespace. **[S]**

Built-in types (`Size` is what sits in the attribute slot; "ref" means u32 offset into the segment, 0 is null): **[S]**

| Id | Name | Size | |
|---|---|---|---|
| `0x80000001` | `xs:boolean` | 1 | |
| `0x80000002` | `xs:unsignedByte` | 1 | |
| `0x80000003` | `pe:CharT` | 2 | UTF-16 unit |
| `0x80000004`, `5`, `6` | `xs:short`, `xs:int`, `xs:long` | 2, 4, 8 | |
| `0x80000007`, `8` | `xs:float`, `xs:double` | 4, 8 | |
| `0x80000009` | `xs:decimal` | 4 | Meaning **[?]** |
| `0x8000000A` | `xs:dateTime` | 8 | |
| `0x8000000B` | `xs:string` | ref | Section 7.2 |
| `0x8000000C` | `pe:GuidT` | 16 | .NET mixed-endian |
| `0x8000000D`, `E` | `pe:BlobT`, `pe:XmlT` | ref | Section 7.3 |
| `0x80000012`, `13`, `14` | `xs:ushort`, `xs:uint`, `xs:ulong` | 2, 4, 8 | |
| `0x80000015` | `pe:MapT` | ref | Section 7.5 |
| `0x8000001F` | `pe:CoreTextAttributeT` | ref | Section 7.6 |
| `0x80000016` | | | Seen as a map value type in 6 HMI objects; layout **[?]** |

An attribute whose type is an `ObjectType` takes no space: relations hold it (section 8). **[S]**

**Layout of a type** **[S]**, confirmed by decoding every object **[S]**:

1. Collect the `Implements` of the type, then of its `Base` types recursively. An interface may be implemented several times.
2. Walking the bases, `primary` holds for a type while each step goes through its `primary="true"` base, or it has no primary base.
3. An attribute is persistent if `constant` is false (the `Implements/Attribute` value wins over the `AttributeSet/Attribute` value) and it is not `intrinsic`. A `virtual` attribute also becomes persistent when a later primary implementation sets `constant="false"`.
4. Sort the interfaces by full name, ordinal.
5. Within an interface, attributes keep their schema order. Offset starts at 4 and grows by the size of each persistent attribute.

**Relations** are declared on `ObjectType`: `<Relation name id cardinality><Target ref/><Inverse .../></Relation>`. `<Inverse name id cardinality/>` declares a relation on the target type; `<Inverse ref="X"/>` names an existing one. **[S]**

## 7. An object's body

Offsets in the body are **from the start of the frame**, not the body. **[S]**

### 7.1 Offset table

`OffsetCount` u32s at frame offset 44. With `n` = the number of interfaces of the type: **[S]**

| Entry | Holds |
|---|---|
| `0` to `n-1` | Interface segments, in sorted order; 0 means absent. |
| `n` | One-to-one relation list |
| `n+1`, only if info bit 0 is set | Low-filled relation list |
| the rest | One list per to-many relation, in schema order (section 8), or for a `CoreText` type its text blocks. **[U]** for the text blocks |

**Interface segment**: u32 segment size, then the persistent attributes at their offsets (section 6). All "ref" offsets count from the segment start. A segment **may be shorter than the schema says**: the attributes past its end are absent, not an error. This is an object written under an older schema. **[S]** Do not read them: it is what makes a strict parser fail on some library objects.

Segments are not always contiguous; follow the table. **[S]**

The frame ends in `0xFF` in most cases, but do not rely on it. **[S]**

### 7.2 Strings

varint `L`, then `L - sizeof(varint)` bytes of UTF-8: `L` counts its own bytes. **[S]**

### 7.3 Blobs (`pe:BlobT`, `pe:XmlT`)

varint total (counts itself, 0 or itself alone means empty), then: **[S]**

| Offset | Size | Field |
|---|---|---|
| 0 | u8 | Kind |
| 1 | u64 | Decoded length |
| 9 | u32 | Page offset; meaning **[?]** |
| 13 | u16 | Page size, normally 4096 (0 or negative means 4096 **[U]**) |
| 15 | u32 | Page count |
| 19 | varint `b` | Number of bitmap words |
| | `b`×u32 | Bitmap: bit `i` set means page `i` is stored |

Then by kind:

| Kind | Name | Rest |
|---|---|---|
| 0 | Small | Stored pages one after another, raw. An unset bit is a page of zeros. **[S]** |
| 1 | CompressedSmall | For each stored page: varint length, zlib stream. **[S]** A page starting with the u32 `0x6007BDED` is raw deflate after that magic **[U]**; none in the samples. |
| 2, 3 | Array, CompressedArray | Never seen. **[U]** |
| 4 | **Large** | **(type:u32, id:u64)** × `(count + 31) / 32`, each a `0x70013` Segment. **[S]** A Segment body has the head above with kind 0 or 1 but no leading varint, its own length, and up to 32 pages. Concatenate them; the result must have the decoded length. |

Kind 4 is not a plain value: reading it as a varint length and bytes loses or corrupts everything above 128 KB.

Kind 4 appears from just over 128 KB (more than 32 pages) in the samples, smallest 131,730 bytes. Every `0x70013` frame is reached this way, and none of the 512 referenced from declared attributes twice. **[S]**

### 7.4 Structures, arrays, enumerations

- **Structure** (ref): u32 size, then elements in schema order, inline; their refs count from the structure start. **[S]**
- **Array** (ref): u32 size, u32 count at offset 4, elements from offset 8. **[S]**
- **Enumeration**: stored as its `base` (default `xs:int`), inline. `flags="true"` means a bit set. **[S]**

### 7.5 Maps (`pe:MapT`)

Decode as a blob, then: u32, u32 count `c`, u32 `o`; `c`×u32 value type ids; `c` strings (section 7.2) of names; values at `o` by static size, whose refs count from `o`. **[S]**

### 7.6 Core text (`pe:CoreTextAttributeT`)

A text per language. varint; then u32 size, u32, u32 default index; a string table; u32 type, u32 `s`, `s` bytes subtype; if the size allows more, u32 flags and a second string table.

String table: u32 `n`; `n`×u16 language ids (LCID: 1033, 1031, ...); `n`×u32 present flags; for each present one, u32 length and UTF-8 bytes. **[S]** A language can be present with no bytes, an empty text. Every project language of the reference project `Texts` (section 17) reads back as TIA's SimaticML export gives it. **[S]** LCID 65535 is no project language but a text for every language: an alarm class's display name is stored there alone, and TIA's export of the classes gives that text (section 25). **[S]** Alarm texts hold an empty one beside their languages. A reader shows a language's own text, else the 65535 text.

**Project languages.** The project's `ProjectData` points to its languages: `UsedCultures` to each project language in the project's order, `EditingCulture` to the editing language, `ReferenceCulture` to the reference language. Each is a `CoreCulture` with `ICoreCulture.LCID` and `ICoreCulture.Name` (`de-DE`), held by the one `CoreCultureContainer`, which also holds languages that are not the project's. The cultures point back (`CultureIsUsedBy`, `IsEditingCultureIn`, `IsReferenceCultureIn`), to the project's `LibraryData` too. **[S]**: `Texts` sets en-US, de-DE and fr-FR, German editing and English reference, through Openness, and these relations say so. A global library has the same relations on its `LibraryData`, by the schema. **[I]**

### 7.7 Expando interfaces

An `AttributeSet` with `expando` holds attributes not in the schema.

**`expando="true"` or `"dictionary"`**: the type's key table is found through `0x70002` then `0x70011`. **[S]**

| Offset | Field |
|---|---|
| 0 | u32 segment size |
| 8 | u32 count `c` |
| 18 | `c`×u32 keys |
| 18+4`c` | `c`×u32 values: inline if the type is fixed and at most 4 bytes; otherwise an offset from 18+8`c` (`0xFFFFFFFF` is missing) |

The key table gives each key's value type, a built-in type id of section 6. An out-of-line structure or array sits at that offset without a further ref. A blob or XML value sits there as in section 7.3, a string as in section 7.2, a `xs:dateTime` as 8 bytes of ticks as in section 2. **[S]**

Some of what a source file shows is only here. Names are keys in the object type's table:

| Object | Key | Holds |
|---|---|---|
| `InterfaceVersionRootData` | `Values` | XML: the start values its members are declared with (section 11) **[S]** |
| `DataBlockData` | `NonRetain` | Bool: a source file's `NON_RETAIN` **[S]** |
| `DataBlockData` | `DBAccessibleFromOPCUA`, `DBAccessibleFromWebserver` | Bool: false adds that block attribute. Absent means true. **[S]** |
| `DeviceItemData`, `OpcUaServerConfigurationData` | the OPC UA settings | Section 18 |
| `DeviceItemData` | `AM_<n>_AddrData_<m>`: an I/O address, a structure | Section 19 |
| `NodeData`, `MastersystemData` | an interface's addresses; an IO system's number | Section 19 |

**`expando="simple"`**: names are in the value itself. **[S]** This is where interface comments are.

| Offset | Field |
|---|---|
| 8 | u32 count `c` |
| 18 | `c`×u32 value type ids |
| 18+4`c` | `c`×u32 offsets of names, from the segment start |
| | Names: NUL-terminated UTF-16LE |
| `v` | Right after the last name's NUL: `c`×u32 value offsets, from `v+4c` |

## 8. Relations: the object graph

This is what ties a block to its source. **[S]**

**One-to-one and low-filled lists**: u16 byte size, u16 used count, then 16 byte slots `relation:u32, target type:u32, target id:u64`. A slot with relation 0 is free. A slot with a relation and type 0, id 0 is an **empty place in an ordered list**: keep it, because indexes count it (section 11).

Low-filled holds to-many relations with few targets, in one list; relations appear in position order.

**A per-relation list** (a to-many relation once it no longer fits low-filled): u32 size, u32, u32 count, then `target type:u32, target id:u64`. The relation comes from the list's position, `k` = entry - `n` - 1, minus one more when info bit 0 is set:

Take the type's to-many relations: its own and its bases' `Relation`s, and those `Inverse` elements declaring one on it, with `cardinality` other than `"1"` and not `abstract`. Name each `<declaring type's full name>.<relation name>`, sort ordinally, drop duplicate names. `k` indexes that list. **[S]**

**Relation names**: `id` on `Relation`, or on an `Inverse` with an `id`.

## 9. The index: `PEData.idx`

A 98 byte header as in section 3, type 128, then 38 byte entries: **[S]**

| Offset | Size | Field |
|---|---|---|
| 0 | u32 | Type |
| 4 | u64 | Id |
| 12 | 16 | Guid |
| 28 | u8 | Node |
| 29 | u32 | Frame offset in `.plf` |
| 33 | u32 | Cluster |
| 37 | u8 | Flags, as frame states plus the index-only bits |

After a commit or close entry comes a u32 `m` and `m` - 4 more bytes. **[S]** File type 2 uses 21 byte entries without guid and node. **[U]**

## 10. Finding blocks

Use type and relation names, never ids: ids come from the file's schema.

| Full type name | Is |
|---|---|
| `Siemens.Simatic.Lang.Model.Blocks.CodeBlockData` | FB, FC, OB, SFB, SFC |
| `...Blocks.DataBlockData` | DB |
| `...Blocks.UserTypeData` | UDT |
| `...Blocks.CompileUnitData` | One network, or one SCL body |
| `...Structures.InterfaceVersionRootData` | Interface root |
| `...Structures.InterfacePartData` | A section of an interface, or a struct |
| `...Structures.InterfaceCommentsPartData` | Member comments |
| `...Idents.IdentContainerData`, `...IdentPartData` | Identifier table of a block |
| `Siemens.Automation.ObjectFrame.CoreText` | A multi-language text |
| `Siemens.Automation.DomainModel.FolderData` | A program folder |

Attributes of a block **[S]**:

| Interface | Attribute | |
|---|---|---|
| `...ObjectFrame.ICoreAttributes` | `Name`, `Comment` (title), `Subtype`, `IsKnowHowProtected`, `Protection` | `Subtype` for UDT and DB is the kind |
| `...Blocks.IGeneralBlockSourceData` | `BlockType` (FB, FC, ...), `Number`, `BlockLanguage` (SCL, F_FBD, DB, ...), `OnlySymbolicAccess`, which a source file shows as `S7_Optimized_Access` | `FALSE` and `TRUE` agree with TIA's generated source in the reference projects (section 17) **[S]** |
| `...Blocks.IPlcHeaderData` | `HeaderAuthor`, `HeaderFamily`, `HeaderName`, `HeaderVersion`: a source file's `AUTHOR`, `FAMILY`, `NAME`, `VERSION` | Agrees with TIA on one block **[S]**. Most UDTs do not have this set |

Relations from a block **[S]**:

| Relation | To | |
|---|---|---|
| `Parent4SourceData.Sources` | `CompileUnitData` | Its source. Order by `ICompileUnitData.RefID`. |
| `BlockInterfaceBaseData.CurrentInterface` | `InterfaceVersionRootData` | Its interface. |
| `BlockInterfaceBaseData.InterfaceComments` | `InterfaceCommentsPartData` | Member comments. |
| `GeneralBlockSourceData.BlockComment` | `CoreText` | Block comment, attribute `ICoreTextRepository.Text`. |
| `CoreObject2IdentContainer` (id `0x222202`, an `Inverse`) | `IdentContainerData` | The block's own identifiers; `IdentContainerData.IdentParts` leads on. |
| `FolderElementData.AggregatingFolder` | `FolderData` | Walk upward for the folder path. |
| `CoreObject.Target` | `S7ControllerTargetData` | The PLC. |
| `CoreObject.Environment` | `ProjectData` or `LibraryMasterCopyData`, ... | Project, or the library copy it lives in. **A master copy can hold a whole other program**, so one name can occur several times for one PLC. |

An OB's **event class**, what starts it, is in its `Subtype`: `OB.` and the class, such as `OB.ProgramCycle`, `OB.Startup`, `OB.CyclicInterrupt` or `OB.DiagnosticErrorInterrupt`, with `.Safety` after it on a fail-safe OB (`OB.CyclicInterrupt.Safety`). Its relation `HandledEvent` leads to an object of the matching type (`ProgramCycleEventData`, `StartupEventData`, ...). The two agree on all 37 OBs of the demo project and of a V20 and a V21 archive that hold the same program, nine event classes among them, closed OBs included. **[S]** Every program cycle OB there is OB 1; a reader that finds no event class takes OB 1 as the program cycle OB. **[I]** That the part after `OB.` is TIA's own name of the class, as an export's `SecondaryType` gives it, is **[I]**: the demo project's OB was not compiled, so TIA exported no SimaticML for it.

A **stub** is a block with state `0x20` and no number, source or interface, holding `CoreObject.TRefParent`: a reference to a block held elsewhere (master copy, library type). Leave it out when listing blocks, but know that a count over all block objects includes it (section 17). **[S]**

Do not treat `SWObjectData2TypeOperandIdentContainerData` (id `0x22220c`) as "my identifiers": that is every container whose block references this one.

## 11. The interface

`InterfaceVersionRootData.UsedParts` is **positional, with empty places**. A `SubPartIndex` in the XML is an index into it. **[S]** Attribute `IInterfacePartData.Payload` holds the XML, `Kind` says what it is: `BlockSource`, `DBSource`, `DataTypeSource`, `InputSection`, `OutputSection`, `InOutSection`, `StaticSection`, `TempSection`, `ConstantSection`, `Structure`, `Values`, `Comments`.

```xml
<Root InterfaceGuid="...">
  <Externals><ExternalType SubPartIndex="6" Name="SomeUdt" BlockClass="UDT" CRC=".." OffsetCRC="..">
    <Usage Path="71" Name="someMember" Section="InOut"/></ExternalType></Externals>
  <Member ID="2" Name="Input" SubPartIndex="0" StdO="0"/>      <!-- a section: no RID; the part is UsedParts[0] -->
  <Member ID="7" Name="Return" n="1"><Member ID="51" Name="Ret_Val" RID="0x02000000" LID="9"/></Member> <!-- inline -->
  <Offsets .../>
</Root>
```

A section part or struct part holds `<Member ...>` around its members; a DB's or UDT's root holds its members directly. **[S]**

| `Member` attribute | |
|---|---|
| `ID` | Member id, unique in its part. A path `a:b:c` of ids identifies a nested member. **[S]** |
| `Name` | **[S]** |
| `RID` | Type code, below **[S]** |
| `Type` | Type text for what `RID` cannot say: `"SomeUdt"`, `Array[1..10] of X`, `String[4]`, also with constants in the bounds. Quotes are `&quot;`. **[S]** |
| `SubPartIndex` | With `RID` and no `Type`: a struct, the part holds its members. With `Type`: where the type's own interface is. **[S]** |
| `MFlags` | The member's attributes, bit by bit, see below. **[S]** |
| `Remanence` | `Retain`: in a retained section. `Classic`: on a static member, in `VAR DB_SPECIFIC`; on an overlay it says nothing. **[S]** |
| `AtMemberID` | It overlays that sibling: `name AT other : type`. **[S]** |
| `StdO`, `LID`, `v`, `n`, `ph`, `Hidden`, `SlotNo`, `Depth`, `ParentId` | Offsets and flags. `StdO` looks like a bit offset in standard access **[I]**; the rest **[?]**. |

**Type codes** (`RID`). The file states them itself: an ident's `<TD T="Bool:33554433:Bool"/>` is `name:decimal code:display`. Read the table from there, it is exact for that file. **[S]** Elementary, `0x020000xx`:

`01` Bool, `02` Byte, `03` Char, `04` Word, `05` Int, `06` DWord, `07` DInt, `08` Real, `09` Date, `0A` Time_Of_Day, `0B` Time, `0C` S5Time, `0E` Date_And_Time, `13` String, `16` Any, `1D` Timer, `1F` IEC_Timer, `30` LReal, `31` ULInt, `32` LInt, `33` LWord, `34` USInt, `35` UInt, `36` UDInt, `37` SInt, `3C` ARef, `3D` WChar, `3E` WString, `3F` Variant, `40` LTime, `41` LTOD, `42` LDT, `43` DTL, `88` TYPE_ID, `90` HW_ANY, `91` HW_IOSYSTEM, `97` HW_SUBMODULE, `9B` HW_INTERFACE, `AA` CONN_OUC, `D0` DB_ANY. A file names only the types its identifiers use, so the reader falls back on the elementary codes of this list, which a test block with a member of each type confirms against TIA's source; `3D` WChar is from there. **[S]** `02000000` never appears in such a table; it is on `Ret_Val` of an FC without a return value, so Void **[I]**. `A0` is in none either: only an OB's `Csg_OBNr` input uses it, and `D0` its `DBNr`. TIA's export of such an OB calls them OB_ANY and DB_ANY. **[S]**

Others: `0x0201xxxx` array of elementary or system type `xxxx`; `0x020002xx` system data types (`20B` TCON_IP_v4, with `Type`); `0x91` UDT, `0x92` DB, `0x93` FB, `0x94` FC, `0x95` OB. `0x9N01xxxx` in the `0x91` to `0x94` ranges is a struct inside that block. **[S]**

**Start values** live in two places, as `<V p="53" v="16#0B"/>`, where `p` is the id path, with `[i,j]` after an array's id. `Display`, `Scope`, `IdentRefID` hold a symbolic value, and a source file writes `Display`. **[S]**

- **Declared** with the member: the root's expando `Values` (section 7.7). This is where a UDT has them, in none of its interface parts. **[S]**
- **Set** in the block: a `Values` part, or a `<Values>` in the root. In a block other than a data block they too are what the declaration says. In a data block they are what it starts with, and a source file lists them after `BEGIN`. **[S]**

A source file writes a declared value after the type: **[S]**

| Member | Written |
|---|---|
| Elementary | The value |
| Struct, array of structs | Nothing: its members have their own lines. In an array of structs the first element's value stands on the member's line. Another element's values are not shown |
| UDT or FB | `(a, b, c)`, one place per member, `()` where nothing is set. Nothing at all if no place has a value |
| Array | `[a, b, c]` in reading order. A plain array stops at its last value, fills gaps with its type's zero, and writes equal neighbours as `3(a)`. An array of a UDT writes all its elements, and each in full |
| Array with nothing set, inside a UDT | `[()]` |
| UDT inside a UDT | `()` if nothing is set in it for any element, else its places. A system type always shows its places |
| Struct inside a UDT | `()`, even with values stored |

Array bounds that are constants resolve through `<Constants><Constant Display Value/></Constants>` at the top of the root that uses them. **[S]**

**Member attributes.** A source file writes them in braces after the name, in this order and only when they differ from the default: `InstructionName`, `LibVersion`, `ExternalAccessible`, `ExternalVisible`, `ExternalWritable`, `S7_SetPoint`. They start right after `{` if an instruction's come first, after `{ ` if not. FCs and OBs show no access attributes, and neither do temporary or constant members anywhere. **[S]**

| `MFlags` | Written |
|---|---|
| none | As the parent struct: the access attributes pass down |
| without both `4` and `8` (`0`, `3`, `7`, `32`, `39`, `512`, ...) | Nothing about access: as the parent struct |
| `4` and `8`, `1` clear (`14`, `76`, `526`, `588`) | `ExternalAccessible`, `ExternalVisible`, `ExternalWritable` all `'False'` |
| `4` and `8`, `1` set and `2` clear (`13`, `77`, `525`, `589`) | `ExternalVisible := 'False'` |
| `4` and `8`, `1` and `64` set (`77`, `79`, `589`, `591`) | `ExternalWritable := 'False'` |
| with `512` set | Also `S7_SetPoint`: `'True'` if the member is elementary or an array of it and not an instruction, else `'False'`. Not on a parameter or a plain struct |

So the value `1` is Accessible, `2` Visible, `64` not Writable and `512` the set point, read only when `4` and `8` say the access bits are stored. Under a closed member every member is closed, whatever its own flags: TIA writes all three `'False'`. An overlay with no `MFlags` of its own takes those of the member it overlays. **[S]** The bits were read off three test data blocks generated in TIA Portal V21 on a copy of the demo project, one member for each combination of the four attributes, alone, together and inside a struct and an array: the reader's source is TIA's generated source byte for byte. A's dump does not change with this table, so the comparison of section 17 still holds. TIA ties the attributes as in the tag table: a member that is not accessible is neither visible nor writable. What a struct or a UDT's `S7_SetPoint` depends on is not fully known: on two blocks a member of a fail-safe UDT has `527` and TIA writes nothing. **[?]**

A member whose part (`SubPartIndex`) has `IInterfaceLibraryData` is of a system type: `InstructionName` is its type's name, `LibVersion` the first two numbers of `IInterfaceLibraryData.Version`. **[S]**

**Names** are written plain if they are identifiers, in any alphabet, and quoted if not or if they are reserved, such as `"Byte"` or `"Name"`. The reader's list of reserved words comes from what this project uses plus the words of a source file, so it is **[I]**. Type code `LTOD` is written `LTime_Of_Day`. **[S]**

**A data block of a type** (`IGeneralDataBlockSourceData.Type` is `DBOfType` or `IDBofFB`) has its type through `GeneratedFromMaster` (a UDT) or `IsGeneratedFrom` (an FB). TIA's source names the type on a line of its own, and lists only the values set in the block between `BEGIN` and `END_DATA_BLOCK`: `<V p="53:2[1]">` becomes `S.y[1] := ...;`, the text as stored, in member order and then index order. A path goes on into the member's own type: `"1st".Part.Item[1].x`. A value of a `DTL` is preceded by one line for each part, `x.YEAR := ...;` and so on to `NANOSECOND`, which are not stored; `WEEKDAY` counts from Sunday as 1. **[S]**

A **shared DB** declares its members in `VAR` sections if optimized, with retained ones in a `VAR RETAIN` of their own, and in one `STRUCT` if not, and then lists its set values the same way. **[S]**

A **DB of a type** has a root of kind `Values`: `UsedParts[0]` is the root of the UDT or FB, whose members and parts apply. With nothing set the root has no payload at all. **[S]** Its `<V>`s list only some start values. SimaticML lists the same ones: a member's `StartValue` is there only if it is set in the block, even with `ExportOptions.WithDefaults`, so what TIA shows comes from the block, else the type, else zero. **[S]** **All of them** are in the compiled image, `LoadablePlusDataBlockData.ILoadablePlusDataBlockData.VolatileBuffer`, reached by `Parent4LoadableBinaryData.LoadableBinaries`: an 8 byte head, then values little-endian at the sum of the optimized bit offsets along the path, `<Offsets><o o="bits"/>` in member order. Checked for `Bool` and integer types in optimized DBs generated from a UDT against what TIA's text implies: the block's value, else the UDT's, else zero. All 122 agree. **[S]** It does not hold for an instance DB, whose offsets count within its sections, and the reader does not use it there, for a protected block, or when the image is shorter than the UDT's `optSize`. A retained DB keeps its values in `RetainBuffer` instead, which the reader does not read. Other types and standard-access DBs: **[U]**. A standard DB's `ClassicBuffer` looks like its values big-endian. **[?]**

A **DB of a system type**, such as the `IEC_TIMER` a `TON` with a data block of its own uses, has that type's root as `UsedParts[0]`, and its `IInterfaceLibraryData.Version` (`1.0.3;S71500,S71200`) gives the version. TIA's source names the type first among the block's attributes and without quotes: `{InstructionName := 'IEC_TIMER';`, ` LibVersion := '1.0';`, ` S7_Optimized_Access := 'TRUE' }`, then `IEC_TIMER` as the type. **[S]**, on the reference project.

**Comments**: `InterfaceCommentsPartData.ICommentsExpandoAttributeSet`, a simple expando (section 7.7) of core texts, keyed by the id path. **[S]**

## 12. SCL source

`ICompileUnitData.Data` with `ProgrammingLanguage` = `SCL`: UTF-8 with a BOM, `<SCLSource Version="3.3.0.0"><Symbols>...</Symbols><RootStatements>...</RootStatements></SCLSource>`.

**It is a concrete syntax tree: walk `RootStatements` in document order, emit each element's text before its children, and the source comes out with its whitespace and comments.** **[S]** The text of an element, first match:

| Element | Text |
|---|---|
| `BL` | `NumBLs` spaces, default 1 |
| `NL` | Line break |
| `LC` | `//` + `TE` |
| `BC` | `(*`; `BCL` its `TE`, `BCE` `*)` |
| A fixed token, below | Its text |
| A container, below | Nothing |
| Anything with `TE` | `TE` (`Statement TE="IF"`, `Const`, `SymPa`) |
| Anything with `ODN` | `ODN` (`SymVa`, `SymPa`, `Sub`, `SymLa`, `Const`), the display name with `#` or quotes already on |
| `FormalName` | |
| `SyId` naming a `<Symbols>` entry with `Name` | That name (an instruction such as `BLKMOV`, in `Sub`) |
| `Statement`, `Label`, `Sub` with none of these | Nothing (`Sub` alone: the call target is the expression before it) |

**Containers**: `RootStatements`, `Statements`, `Fold`, `Expression`, `Param`, `FctCa`, `InstCa`, `CaseElem`, `CaseRange`, `CaseSRange`.

**Fixed tokens**: `OpAs` `:=`, `OpPa` `=>`, `OpPl` `+`, `OpMi` `-`, `OpMu` `*`, `OpDi` `/`, `OpPo` `**`, `OpE` `=`, `OpU` `<>`, `OpG` `>`, `OpL` `<`, `OpGE` `>=`, `OpLE` `<=`, `OpAND` `AND`, `OpOR` `OR`, `OpXOR` `XOR`, `OpNOT` `NOT`, `OpMOD` `MOD`, `OpAAND` `&`, `OpPlAs` `+=`, `OpMinAs` `-=`, `OpMulAs` `*=`, `Colon` `:`, `Comma` `,`, `Dot` `.`, `BracO` `(`, `BracC` `)`, `BoxO` `[`, `BoxC` `]`, `FiSt` `;`, `LDots` `..`, `KwTHEN` `THEN`, `KwELSE` `ELSE`, `KwELSIF` `ELSIF`, `KwENDIF` `END_IF`, `KwDO` `DO`, `KwTO` `TO`, `KwBY` `BY`, `KwENDFOR` `END_FOR`, `KwENDW` `END_WHILE`, `KwOF` `OF`, `KwENDC` `END_CASE`, `KwEndRegion` `END_REGION`.

The tag-to-text mapping is **[I]**: read off the tags and how the result parses. Every SCL element of both V20 samples is covered. `REPEAT`/`UNTIL`, `CONTINUE` and `OpDiAs` occur in no sample, so expect tokens not listed: a reader should show an unknown tag visibly rather than drop it. A further comment form may take its text from a `CoreText` relation of the compile unit, by index; no sample has it. **[U]**

**Statements** carry a semantic `SI`: `STAss`, `STSub`, `IF`, `IFE`, `IFEI`, `IFEIE`, `STRegion`, `STPlAss`, `STMinAss`, `STMulAss`, `STGOT`, `STFOR`, `STRET`, `STSim`, `STCAS`, `STEXI`, `STWHI`. `Expression` adds `Result` (the type) and `SI` (`ExprPrimD`, `ExprDot`, `ExprAnd`, ...), `SymVa` `SI` the access kind (`Var`, `VarStruct`, `VarArray`, `VarDB`, ...), `Param` `ParaIn`, `ParaOut`, `ParaInOut`. So a reader can build a real AST and need not reparse the text. `Deco`, `UId` are not needed for text. **[S]**

**Calls.** `FctCa` holds a `Sub` with `SI="FC"` whose `ODN` is the called function, or no `ODN` and a `SyId` naming an instruction. `InstCa` holds what is called first: a `SymVa` with `SI="VarDB"` for an instance data block, or a `Sub` with `SI="FB"` whose `ODN` is `#member` for a multi-instance, whose `Type` in the interface is the function block. So a reader can tell a call from any other use of a name without parsing text. **[S]** for the forms above, checked by opening the real archive; a call through a nested member, `#a.b()`, names only `b` and is not followed. **[I]** A part after a `Dot` can be quoted too, `"Db"."Int"`, and is no block's name. **[S]**

**`<Symbols>`**: one entry per symbol by `SymID`. `RefId` points into the identifier table (section 14). Kinds: `LocalTag`, `LocalStructuredTag`, `GlobalTag`, `GlobalStructuredTag`, `GlobalUDTTag`, `LocalConst`, `GlobalConst`, `LiteralConst`, `BlockParameter`, `LibraryParameter`, `BlockInterface`, `Instance`, `Instruction`, `FCBlock`, `FBBlock`, `DBBlock`, `UDT`, `Type`, `Label`, `InternalUnknown`. `ParentSymID` builds the access path. **[S]**

Other values of `Data`: empty (no source stored), `<FlgNet` (section 13), `<EncryptedData` (section 15).

**As TIA writes it.** TIA's *Generate source from blocks* puts this around the text: the block line, `TITLE`, the block attributes in braces, the header fields that are set (`VERSION` is `0.1` if none is stored), `NON_RETAIN` on a data block that is, then the block comment with `//` directly before each line. Each section is `   VAR_IN_OUT ` (with a trailing space) up to `   END_VAR` and a blank line, one more blank line follows the last section, then `BEGIN`. `VAR RETAIN`, `VAR DB_SPECIFIC` and a UDT's `STRUCT` have no trailing space. The sections of one kind follow each other with no blank line between them. Every line of the body, empty ones too, starts with one tab, and a body with no text has no line at all. A file ends with `END_...` and a blank line. An OB's inputs are left out; the non-optimized OBs of the sample have none stored, so that part rests on optimized ones. **[S]**

TIA writes titles and comments in one language, the project's editing language, and leaves out one that is empty there. **[S]**: the source TIA generated for `Texts`, whose editing language is German, is German, and a member comment in English alone is left out. The reader writes the editing language too, or one the user chooses; where that one is empty it takes the reference language's text, then the first project language's that has one, so as not to lose it, and differs from TIA on purpose ([spec 0014](../specs/0014-texts-and-alarms.md), FR-004). Before it read the project's languages it took English first, which was wrong for a project edited in another language.

A UDT is `TYPE`, `   STRUCT`, its members, `   END_STRUCT;`, a blank line, `END_TYPE`, with no `S7_Optimized_Access`. **[S]**

## 13. Networks: LAD, FBD, STL and GRAPH

Each `CompileUnitData` is one network. `ICompileUnitData.ProgrammingLanguage` is its language, `ICoreAttributes.Comment` its title, and the relation `CompileUnitComment` leads to a `CoreText` with its comment. **[S]** Its `Data` names its form by its root element: `SCLSource` (section 12), `FlgNet` for LAD and FBD, `Statements` for STL, and `AddInfos`, `GraphStructure` and `Actions` for the parts of a GRAPH block. **[S]** The reference project `Languages` (section 17) holds blocks in all of them, built and compiled by TIA Portal V21; every title and comment there equals TIA's export.

### LAD and FBD

Languages: `LAD_CLASSIC` and `FBD_CLASSIC`, which TIA calls LAD and FBD, and their fail-safe forms `F_FBD` and `F_LAD`. All are one `FlgNet`, `<FlgNet xmlns="http://www.siemens.com/automation/2015/FunctionLadderDiagram" Version="14.0.0.2" Lang="...">`, and it holds the graph TIA's SimaticML export holds, under other names: **[S]**

| Stored | SimaticML |
|---|---|
| `Part Gate="Contact"` | `Part Name="Contact"` |
| `LRef RefId` with `Instance RefId`: an instruction with an instance, such as `TON` or `CTU` | `Part Name="TON"` with `Instance` |
| `CRef RefId` with `CodeBlock RefId` and, for an FB, `Instance RefId`; `RefId` is the called block's `BlockInterfaceInfo` (section 14) | `Call` with `CallInfo Name` and its `Parameter`s |
| `ORef RefId`, an identifier (section 14); without `RefId` an open connection | `Access` with its symbol or constant; `OpenCon` |
| `PCon UId PinName`, `OCon UId`, `Powerrail` | `NameCon UId Name`, `IdentCon UId`, `Powerrail` |
| `Labels` with an `ORef` per label | `Labels` with a `LabelDeclaration` per label |

```xml
<Parts>
  <ORef UId="24" RefId="1"/>             <!-- an operand: RefId into the identifiers; none means not connected -->
  <Part UId="23" Gate="Coil"><Negated PinName="in"/><TemplateValue .../></Part> <!-- a gate -->
  <LRef UId="21" RefId="5"><Instance UId="22" RefId="4"/></LRef>                <!-- an instruction with an instance -->
  <CRef ...><CodeBlock RefId=".."/></CRef>                                       <!-- a block call -->
</Parts>
<Wires>
  <Wire UId="28"><OCon UId="24"/><PCon UId="21" PinName="IN"/></Wire>
</Wires>
```

A `Wire` lists its ends in no fixed order of source and sink: a coil's wire lists its operand first. What drives a wire is an output pin on it (`out`, `out1`, `q`, `Q`, `ET`, `CV`, `eno`, or a call's output or return parameter), else the power rail, else an operand. **[S]** `Negated` names a pin of its part, an input or an output, so an inverted output inverts what its wire carries. A LAD parallel branch closes in an `O` part; a coil names its operand through the pin `operand`, a jump its label through `label`, an edge its memory bit through `bit`. **[S]** Gates seen: `Contact`, `Coil`, `SCoil`, `RCoil`, `PContact`, `NBox`, `PBox`, `A`, `O`, `Move`, `Add`, `Sub`, `Abs`, `Eq`, `Ne`, `Ge`, `Le`, `Sr`, `Rs`, `Jump`, `JumpNot`, `Return`, and in GRAPH `TrCoil`, `SvCoil`, `IlCoil`.

A network has no text in TIA, so TiaTools writes it in a **statement form** of its own (ADR 0013), each network under `// ---- network n (LAD): title` and its comment:

| Parts | Statement |
|---|---|
| Contacts in series, in parallel, negated; gates `A`, `O`, `X` | `AND`, `OR`, `XOR`, `NOT`, in parentheses only where they must be |
| `Coil` | `#op := <logic>;`, negated `#op := NOT <logic>;` |
| `SCoil`, `RCoil`; `Sr`, `Rs` | `IF <logic> THEN #op := TRUE; END_IF;` and `FALSE`, unconditional on the power rail; a box's set and reset in its order of precedence |
| `PContact`, `NContact`, `PBox`, `NBox` | `P(<signal>, <memory bit>)`, `N(...)` |
| `Eq` … `Le`; `Add` … `Mod`; `Move` | `a >= b`; `#t := a + b;`; `#t := in;` |
| A box with an enable input not on the power rail | its statements inside `IF <en> THEN … END_IF;` |
| An instruction with an instance | `#delay(IN := …, PT := T#2S);`, with a data block of its own `"LampDelay".TON(…);`; an output another part reads is `#delay.Q` |
| A call | `#r := "Limit"(value := …);`, `#pulser(trigger := …, q => #pulse);`, parameters in the order the block declares them |
| `Jump`, `JumpNot`, `Return`; labels | `IF <logic> THEN GOTO skip; END_IF;`, `RETURN;`; `skip:` before the network's statements |
| Anything else | `Name(pin := …, pin => …);`, naming every connected pin |

Checked: all 27 LAD and FBD networks of the reference project `Languages` give the same statements from the archive as from TIA's export of each compiled block. **[S]** So do the 7 fail-safe networks of the reference project `Safety`, 6 in F-FBD and one in F-LAD, compiled by TIA with STEP 7 Safety: a fail-safe network is stored as any other. **[S]** The samples' `F_FBD` networks go the same way, but TIA exports no SimaticML of them (section 17), so they were compared by eye only.

### STL

`<Statements Version="14.0.0.0">`, one `Statement` per line: `Token DispName` is the mnemonic as written (`Kw` numbers it), `OpdAccess RefId` the operand, `UserToken DispName` a constant written in place, `LC DispName` a line comment, `Label` with an `OpdAccess` a label, and `CallInfo RefId`, the called block's `BlockInterfaceInfo`, with a `ParaExpression FPNum` per parameter: its place in that block's parameter list, from 1. `NumBLs` counts blanks as typed; TIA's generated source does not keep them. **[S]**

TIA's generated source writes each network as `NETWORK`, `TITLE = ` and the title, the comment's lines after `//`, then each statement six blanks in: the mnemonic, a blank and the operand, `;`. A line comment follows ` ;//`; a label stands in front, `done:` and six blanks. A call is `CALL "Limit"`, then one parameter per line, `(  ` before the first and nine blanks before the others, the name padded to 28, `:= `, the value and ` , ` but after the last, then `      );`. **[S]**, byte for byte on the reference project's STL block. An STL network inside a LAD or FBD block is stored the same way and written with the same lines. **[S]**

An **operand** is everything in the statement after its mnemonic, in order: `OpdAccess` and `UserToken` as above, and `Token`s of two kinds. A `Token Type="PartialKeyword"` is an area, register or status bit written in place (`MW`, `DBW`, `AR1`, `OV`, `BR`, `DBNO`); a `Token Kw="["`, `","` or `"]"` is the punctuation of an indirect address. TIA writes a `[` with a blank after it and a `,` with a blank after it, so `L MW[ #place]`, `L DBW[ AR1, P#2.0]`, and, with no area before the bracket, no blank after the mnemonic: `A[ AR2, P#0.1]`. A block call with an instance data block is `CALL "Blink", "BlinkDb"`, the same way. A constant is written as TIA normalises it, not as typed: `1.5e+003` becomes `1500.0`, `L#100000` becomes `DINT#100000`; the stored `UserToken` already holds that. **[S]**, byte for byte on the reference project `MoreLanguages`: memory-indirect, register-indirect within and across areas, `OPN DB[ #dbNumber]`, `P##local`, word logic, shifts, conversions, a jump list, a loop, status bits, edges, constants of every format and calls of an FB with an instance data block and as a multi-instance.

A **classic CPU**'s STL (an S7-300 in `MoreLanguages`) is stored the same way. Where an address has no tag, TIA creates one when it builds a block from source, named `Tag__` and a number, and the block uses it; an address it does not tag, such as `%DBW2` after an `OPN`, is an identifier without a name (section 14). **[S]**

### GRAPH

Language `GRAPH_PLUS`. A block's compile units, in order: `AddInfos` with its alarm settings; the permanent instructions before the sequence, as `FlgNet`s without `RefId`; `GraphStructure`; for each step an `Actions` unit and two `FlgNet`s ending in `SvCoil` (supervision) and `IlCoil` (interlock), and for each transition an `FlgNet` ending in `TrCoil`, each with the `RefId` of its step or transition; last the permanent instructions after the sequence. **[S]**

`GraphStructure` holds `Sequence` with `Steps` (`Step UId RefId Init`), `Transitions` (`Transition UId RefId`), `Branches` (`Branch No Type Cardinality`, the types `SimBegin`, `SimEnd`, `AltBegin`, `AltEnd`) and `Structure`, a `Connection` of two of `StepRef No`, `TransitionRef No`, `BranchRef No In|Out`; a jump has `LinkType="Jump"` on the step it goes to. A step's or transition's `RefId` is the identifier of its static member, whose name is its name, so a step cannot share a name with a member. **[S]** A step's number is its `UId` less 100000, a transition's less 200000: what the connections call `No`. **[I]**, since the reference project's numbers run from 1 without a gap.

An action is `Action Event Interlock` holding `Operator Operation`, the qualifier (with `Function`, `ADD` or `CALL`, for an instruction), then `OpdAccess`, `Token Text` and `Blank Num` in the order of its text, and a `CallInfo` of template parameters that are not text. A `Token Text="&#xA;"` ends the action's line. **[S]**

A step's **times**, which TIA's export gives as `MaximumStepTime` and `WarningTime`, are the start values of its static member's `T_MAX` and `T_WARN`, parts of its type `G7_StepPlus_V6`, declared in the block like any start value (section 11): `<V p="<step member>:<part>" v="T#10S"/>`. TIA stores them for every step, its defaults `T#10S` and `T#7S` too. **[S]**, on the seven steps of the reference project `Languages`, all with the defaults, and the three of `MoreLanguages`: one with the defaults, one with both times set and one with only the maximum set.

The statement form lists the permanent instructions before the sequence, then `STEP n name` with its times (`maximum time: T#30S, warning time: T#20S`) and each action as `event qualifier interlocked: text` (`N: #valve`, `D: #lamp, T#2S`, `S1 N interlocked: #lamp`, `N: CALL "Tick"()`) and its supervision and interlock unless empty, then `TRANSITION n name: steps -> steps WHEN condition`, branches resolved into the steps a transition leaves and activates and a jump marked `(jump)`, and last the permanent instructions after. It equals what TIA's export of the reference project's GRAPH block gives. **[S]**

### ProDiag

A **ProDiag function block** has language `ProDiag`; its interface holds `PC_Flags_V2`, `SV_FB_State_V2` and temporary members of `SV_State_V2`, of `Variant` (type code `3F`) and of type code `4F`, whose name is not known. **[?]** TIA adds an OB of language `ProDiag_OB` that calls each ProDiag function block; its one network is `<GlobalCalls>`, a `CRef` per call naming the function block, with an `ORef` naming its instance data block and one leading to the block's `BlockInterfaceInfo`. The statement form writes it as SCL calls a function block with its instance data block, `"PlantDiagDb"();   // "PlantDiag"`. **[I]**: TIA neither generates source for a ProDiag block nor exports it as SimaticML, so nothing of TIA's checks it.

What a ProDiag function block evaluates is stored with the data block, not with the function block:

| Data block | Relation | Holds |
|---|---|---|
| Global | `UDTInstSupervisionContainer` to a `UDTInstSupervisionContainerData` | per member of a PLC data type assigned, a `UdtInstSupervisionRoot` with `UdtInstance RefId`, the member, and `SupervisionFB RefId`, the ProDiag function block |
| Instance | `BlockInstSupervisionContainer` to a `BlockInstSupervisionContainerData` | one `SupervisionFB RefId` for the whole block |

Both keep the XML in `IBlockInstSupervisionContainerData.Data`, `<Supervisions Version="14.0.0.0">`, and their `RefId`s lead into the container's own identifier table (`CoreObject2IdentContainer`, section 14): a `GlobalAccess` such as `"Plant".fans[0]`, an `FBBlock`. A dump lists them after the data block, `// assigned to ProDiag function block "PlantDiag": "Plant".fans[0]`. **[S]**: every assignment in `MoreLanguages`, three members of a global data block and one instance data block, equals TIA's SimaticML export, where it is `AssignedProDiagFB` on the member, on a `Subelement` of an array, or among the instance data block's attributes.

The **supervisions** themselves, on members of a function block or PLC data type, are not read. TIA Portal V21's Openness refuses SimaticML with `<Supervisions>`, in a software unit and outside one alike, and has no other way to create them, so no reference project can hold one. **[?]**

## 14. Identifiers

`IIdentPartData.PayLoad`: `<IdentXmlPart>` with one element per identifier; a part's `Meta` lists its ids. **[S]** `FilcMetaPayload` and `PayLoad` of the container hold a mapping. Each element has `<ID N S RID ...>` (name, scope, id, used by `RefId`) and `<CS><C .../></CS>` usage. **[S]**

| Element | Display |
|---|---|
| `InterfaceAccess`, `MultInstAccess` | `#` + the `AO/@N` path |
| `GlobalAccess` | `"` first `AO` `"` + the rest of the path; `BAD/@BIRID` the block |
| `StructuredTagAccess` | Base `STAD/@TIRD` + path |
| `SimpleAccess`, `AufDBBlock`, `DepDBBlock`, `FCBlock`, `FBBlock`, `OBBlock`, `DataTypeBlock`, `GlobalConstant` | `"N"` |
| `LocalConstant` | `#N` |
| `LiteralConstant` | `CD/CB/@V` |
| `Instruction`, `DirectType`, `Label` | `N` |
| `BlockInterfaceInfo`, `Expression` | Not a name |

An `AO` with `RIDI` is an array element: `RIDI` names the identifiers of its indexes, so `<AO N="fans" RIDI="4">` with identifier 4 the constant `0` is `fans[0]`. **[S]** An `Address` is an address taken, `P##local`: its `AD SSRID` names the identifier it points to. **[S]** A `SimpleAccess` with an empty `N` is an absolute address no tag stands for: its area is `SAD/@R` (`Input`, `Output`, `Memory`, `Data`), its size in bits `OD/@S` and its bit offset `SSD/@AO`, so `R="Data"`, `S="16"`, `AO="16"` is `%DBW2`. **[S]** for that one, from a classic CPU's STL; the other areas and sizes are **[I]**.

A path segment that is not a plain identifier is quoted. `BPIL/BPI` gives a called block's parameters in order, each with its name `N`, section `S` (`Input`, `Output`, `InOut`, `Return`) and type; an STL call numbers them from 1, and a LAD or FBD call names them. **[S]** `TD/@T` gives a type (section 11). A `LiteralConstant`'s `CB/@SV` is the constant as the source writes it, escaped as an XML name (`T_x0023_2S` for `T#2S`), where `V` holds only its value (`2000`). **[S]**

Note that **this table repeats names used in the block**, which is why a plain text search over a whole archive overcounts source occurrences (section 17).

## 15. Know-how protection: read as encrypted

A protected block holds only its ciphertext, so there is nothing to render but the encrypted form; the reader reports it and reads on. Decryption is a separate capability it does not have (ADR 0011).

- `Data` of a protected block starts `<EncryptedData Type="http://...`: report it and stop.
- Still in clear on a protected block in the samples: name, kind, number, language, folder, `IsKnowHowProtected`, and an OB's event class (section 10). Some also have an interface and comments in clear: 93 of 607 protected block objects in A, 22 of 447 in B. The rest, Siemens system blocks among them, have none stored.
- Their identifier parts are in clear (209 in A, 88 in B). The reader takes only type codes from them for its table and never renders them.
- TIA's cross-reference list holds a protected block's uses in clear, its own members' and their places included (section 21). The reader shows which other objects it uses and how, and nothing of the rest (ADR 0015).
- `ICoreAttributes.Protection` is a separate attribute. Use `IsKnowHowProtected` for the flag.
- A technology object under Siemens' own protection (`Protection = SystemKnowHowProtection`: PID controllers, counters) has the flag but stores its interface and values in clear, so it is read like any other (section 23, ADR 0016). An encrypted part stays closed whatever the protection. **[S]**

## 16. Reader recipe

```
frames  = walk(file)                                   # section 4
current = last frame per (type, id), minus deleted     # section 4
schema  = parse(last 0x70000)                          # section 6
expando = 0x70002 + 0x70011                            # section 5
for each current frame with an ObjectType type:
    offsets  = u32[OffsetCount] at frame + 44
    segments = offsets[0 .. n-1] with the type's sorted interfaces    # sections 6, 7.1
    lists    = offsets[n ..]                                          # section 8
for each block (section 10):
    interface = CurrentInterface → Root XML → UsedParts[SubPartIndex] # section 11
    comments  = InterfaceComments → simple expando                    # section 7.7
    units     = Sources, by RefID; Data → SCL, FlgNet, or closed      # sections 12, 13, 15
```

A new implementation is right if it reproduces section 17.

## 17. Checks and numbers

A and B are V20, the library V15.1.

| Check | A | B | Library |
|---|---|---|---|
| Frames, walk ends at end of file, all hashes right | 31,238 | 47,212 | 1,246 |
| Current objects | 31,237 | 47,211 | 838 |
| Distinct ids with more than one frame | 0 | 0 | 160 (max 9) |
| Deleted at last version | 0 | 0 | 199 |
| Index entries; frame offset matches the last frame | 31,238; 31,237 | 47,212; 47,211 | 1,037; 837 (+199 deleted) |
| Relation targets that exist | 192,821 | 295,232 | 2,009 |
| Relation targets missing (rest are empty places) | **0** | **0** | **0** |
| Target type fits the schema's `Target` | 184,760 of 184,760 | 287,254 of 287,254 | 1,941 of 1,941 |
| Per-relation lists among them | 508 | 933 | 1 |
| Relation ids the schema does not name | 0 | 0 | 0 |
| Large blobs: `0x70013` frames reached, of | 593 of 593 | 1,852 of 1,852 | none used |
| Blob kinds | 0, 1, 4 | 0, 1, 4 | 0, 1 |
| Code block objects with `IsKnowHowProtected` (the total includes stubs, none protected) | 511 of 945 (6 stubs) | 349 of 737 (167 stubs) | 4 of 21 (15 stubs) |
| SCL units: tags with no text rule | 0 | 0 | 0 |
| `SymVa/@ODN` agrees with section 14's name | 87,793 of 87,952 | 52,896 of 52,932 | 1 of 1 |

The `ODN` differences are one kind: a nested access where the token holds the last segment only (`#Speed` against `#a.b.Speed`).

**Compared with TIA Portal V21.** `tiadump compare` against `tools/TiaOpennessExport`, on A's two PLCs. Every block TIA would generate source for, after our leading comment lines and before our trailing ones, in bytes once TIA's CRLF is read as LF:

| Kind | Identical | Different | Closed | FBD, no source from TIA |
|---|---|---|---|---|
| FB | 125 | 0 | 92 | 35 |
| FC | 238 | 0 | 24 | 13 |
| OB | 14 | 2 | 2 | 0 |
| Global DB | 40 | 2 | 47 | 0 |
| Instance DB | 50 | 0 | 34 | 0 |
| UDT | 319 | 0 | 10 | 0 |
| **All** | **786** | **4** | **209** | **48** |

The differences: one OB on each PLC whose title and comment have no text in the editing language (section 12), and two global DBs with a fail-safe UDT's `S7_SetPoint` (section 11). The project's V20 archive, upgraded and exported the same way, gives the same table. That is the same program, so it does not test the rules on anything new: most of sections 11 and 12 was worked out from this comparison, and comes from this one project.

Tag tables, from TIA's export of them: all 208 tags and constants in them agree in type, all 66 addresses and all 142 values. The 282 system constants the reader also lists are in no exported table.

**The reference projects.** Thirteen small projects TIA Portal V21 built from sources written for this repository, committed in `tests/reference` with the source TIA generated and its export of each tag table ([spec 0008](../specs/0008-reference-projects.md)). The library's tests compare the reader with them in every run. They hold every elementary type with start values, arrays, nested UDTs, a DB of a UDT, a retained DB and a standard-access DB, an FB with every interface section and constants and its instance DB, a multi-instance, text in German, French, Japanese and Chinese with string escapes, two PLCs with folders four deep, and one know-how protected block. All 34 blocks TIA generated source for are identical, the protected one is closed and shows nothing of its body, and all 27 tags agree in name, type, address, comment and external access. These are new programs, so unlike A's comparison they test the rules on something they were not worked out from.

The sixth, `Languages` ([spec 0009](../specs/0009-logic-languages.md)), holds blocks TIA takes no text source for, imported as SimaticML written for it and compiled by TIA: LAD and FBD with contacts in series and in parallel, negated contacts, inputs and outputs, set and reset coils and boxes, edges, a jump to a label, a move, a comparison, an addition, a timer as a multi-instance and with a data block of its own, a counter, a call of an FC and of an FB; a LAD block with an SCL and an STL network; an STL block with bit logic in brackets, load and transfer, arithmetic, a jump and a call; and a GRAPH block with a simultaneous and an alternative branch, a jump, actions with the qualifiers N, S, R and D, an event, an interlocked action, an assignment and a call, a supervision, an interlock and permanent instructions before and after. Its STL block and the timer's data block are identical to TIA's generated source; its 27 LAD and FBD networks and its GRAPH sequence give the same statement form from the archive as from TIA's SimaticML export (section 13). Invented blocks test the rules on the forms TIA writes, not on the habits of a real program.

The seventh, `Hardware` ([spec 0010](../specs/0010-hardware.md)), holds hardware TIA built through Openness from an invented description: two CPUs, four S7-1500 modules, four ET 200SP devices on PROFINET and one on PROFIBUS, two PROFINET subnets and one PROFIBUS subnet, three IO systems, a device in a device group, one device in no IO system, a start address and a PROFINET device name set by hand, and an IP address set at the device. Its build writes, beside the generated source, what TIA reports about the hardware through Openness, and the tests compare the reader with it (section 19): all 7 devices, all 73 device items TIA lists with their slots, order numbers, firmware and type names, all 11 I/O address ranges, all 9 interfaces' subnets, IP addresses, masks and PROFINET device names, the 3 subnets and the 3 IO systems with their numbers, IO controllers and IO devices agree, and every hardware identifier that a system constant holds sits where TIA puts it.

The eighth, `Tables` ([spec 0015](../specs/0015-tables-and-cross-references.md)), holds a copy of the `Languages` program with a global data block of a UDT, an FC using its members, an instance data block called from `Main`, and a protected FC that calls another and writes a tag; and two watch tables and the force table, imported as SimaticML: entries on tags, data block members, an array element, an address with no tag, five display formats, three triggers, values, comments and a comment line, and a table in a folder. All 14 entries agree with TIA's export of the tables (section 20), and 169 rows of TIA's cross-reference list, from each block's and each tag's side, with the list the reader reads from `XRef.db` (section 21).

The ninth, `Safety` ([spec 0012](../specs/0012-safety.md)), holds two F-CPUs. `PLC_Safety` has an F-DI and an F-DQ in its rack and an IO device with an F-DI, two F-runtime groups with cycle times of their own, F-blocks in F-FBD and F-LAD, a fail-safe global DB, an F-FB as a multi-instance, fixed F-block number ranges, a hand-set F-monitoring time and F-destination address, and a standard OB that reads the fail-safe DB; TIA compiled its safety program. `PLC_Locked` has one F-runtime group, and an invented safety password set after compiling. Every value of `tiadump safety` that Openness also reports, 485 of them, equals it, every one of the 77 fail-safe blocks with its F-signature among them; its 7 fail-safe networks give the same statement form as TIA's export; and neither the invented password nor what the archive stores of it appears in the report or the dump (section 22).

The tenth, `TechnologyObjects` ([spec 0013](../specs/0013-technology-objects.md)), holds technology objects TIA created through Openness on a CPU 1517T-3 PN/DP at V3.1 and a CPU 1511C-1 PN: a speed, a positioning and a synchronous axis and an external encoder at 8.0, PID_Compact 3.0, PID_3Step 2.3, PID_Temp 1.1, a high-speed counter 4.0 on the CPU's own counter channel and an SSI absolute encoder 3.1, each with parameters set away from their defaults, and an SCL block that reads two of them. The build writes the value Openness reports for every parameter of each; of the 925 that are members of a data block and have a value in the archive, 921 agree with the reader's, and the other 4 are given by Openness in other units than the data block holds (section 23). Measuring inputs, output cams and cam tracks are not in it: Openness V21 neither creates nor imports them.

The eleventh, `MoreLanguages` ([spec 0016](../specs/0016-remaining-languages.md)), has two PLCs. An S7-1500 holds a GRAPH block with step times, an STL block with indirect addressing in a block with standard access, an STL block with the other forms section 13 lists, a global and an instance data block assigned to a ProDiag function block, and that function block with the OB TIA adds for it. An S7-300, CPU 315-2 PN/DP, holds an SCL function block and its instance data block, an STL function with an S5 timer, a counter and a data block opened by number, a LAD and an FBD block on global tags, a shared data block with standard access and a PLC data type. All 14 blocks TIA generated source for are identical, the LAD and FBD networks and the GRAPH sequence with its step times give the same statement form as TIA's export, and the four ProDiag assignments agree. TIA exports neither source nor SimaticML for the ProDiag function block or its OB, which `known.tsv` lists. CFC was not built: the TIA Portal V21 at hand has no CFC editor, and Openness takes no CFC chart.

The twelfth, `Hmi` ([spec 0011](../specs/0011-hmi.md)), holds a Unified MTP700 panel on an S7-1500 and the WinCC RT Advanced runtime of an ET 200SP Open Controller on its software PLC: two HMI connections, 13 HMI tags (internal, linked to a PLC tag, to a data block member in a struct and to an array element, and by absolute address), two discrete and two analog alarms, and three screens with I/O fields, texts and a button. Every one of them, with each attribute section 24 marks **[S]**, agrees with what TIA's Openness gave for it when it built the project. On the two local samples with a Comfort/Advanced device the report reads every tag and resolves every link to a data block member, and finds the bound tags of 708 of 1,455 screen items, in half a second.

The thirteenth, `Texts` ([spec 0014](../specs/0014-texts-and-alarms.md)), has three project languages with German as editing language: an SCL block with its title and comment in all three, a LAD block and a global DB in SimaticML with network and member texts in all three or left empty in some, tags commented in several languages, two PLC text lists, and program alarms with instance texts, one of them in a protected block. Its SCL block and data blocks are identical to TIA's generated source, in German; the global DB `Recipe` is listed as known, because the reader writes an English comment TIA's German source leaves out.

The text is the same after compiling. TIA exports SimaticML only for a compiled block, so a retrieved copy was compiled block by block, with every block that is not fail-safe, and closed unsaved. It refuses to compile the whole PLC without permission for the safety program. That gave SimaticML for all 319 UDTs, 85 of 92 open DBs, 194 of 379 open SCL blocks, and the same table as above.

**What this does not cover.** FBD networks: all 48 in the project are `F_FBD`, so they stay uncompiled and TIA exports none of them; fail-safe networks are compared on the reference project `Safety` instead. Protected blocks, by choice. LAD, plain FBD, STL and GRAPH: none in the project are open.

**Values still not decoded, by design or not understood**: `0x80000016` in 6 HMI objects of B; 24 keys unknown in an HMI `StyleData` simple expando; `XRef.db`'s `addrs` table and some access codes (section 21); `MetaId`; `0x70003` to `0x70006`. In the V15.1 library 12 `DataBlockData` objects do not fit the schema for some attributes (`Subtype`, `Culture`, `Password`, `Version`, `ITagAddress`), whichever of its three schemas is used: an offset leads out of the frame. A reader must treat such a value as absent. **[?]**

**Whole-archive search against this reader.** `TCON_IP_v4` in A: 153 in decoded values, 118 of them identifier parts, 14 TIA task card settings, 10 interface payloads, and **0 in source text**; it is the declared type of 4 members in 3 blocks. So a text hit says little about code. `ActiveEstablished` in A: 23, of which 12 in source (2 blocks), 7 identifiers, 4 interfaces. B: 60 and 11, 3 in source (1 block). `TCON_IP_v4_SEC` and `ActivateSecureConn` occur nowhere in either, decoded values and source alike.

**How far the format is version-bound**: the same reader reads V15.1, V20 and V21 unchanged. A, upgraded from V20 to V21, gives byte-identical source for all 963 block files and 14 tag files; only the system instruction list and object ids changed; frame layout, blobs and relations did not move. On V15.1 kind 4 blobs do not occur and per-relation lists occur once, so those two rest on V20. Versions before 1400 use other headers (section 3), and none was tried.

## 18. OPC UA

Where a PLC's OPC UA configuration lives. Found by walking every object of C, D and E, then checked in TIA Portal V21 on a copy of the demo project (CPU 1516-3 PN/DP, firmware V2.9): through Openness each setting of the CPU's OPC UA item, and each access column of a tag, was changed, the copy archived, and what it stored read with `tiadump inspect`. A key marked **[S]** below appeared, or changed, with its setting. `tiadump opcua` reports it all. This section names types and keys only, never what a sample stores in them.

**The server's settings** are dictionary expando values (section 7.7) of the CPU's OPC UA device item: a `Siemens.Simatic.HwConfiguration.Model.DeviceItemData` whose `ICoreAttributes.Subtype` is `S71500.CPU.OPC_UA` and whose `IConfigBaseData.ConfigObjectTypeName` is `S71500\Submodules\CPU\OPC_UA\MDD_OpcUa_<version>`. `CoreObject.Target` leads to its PLC. Every PLC of the samples has one. **[S]**

| Key | Type | TIA's setting (Openness attribute) | Default |
|---|---|---|---|
| `OpcUaEnableServer` | bool | Activate OPC UA server (`OpcUaServer`). Absent counts as off. **[S]** | off |
| `ApplicationName` | string | Application name (`OpcUaApplicationName`). **[S]** | `SIMATIC.S7-1500.OPC-UA.Application:<PLC>` |
| `OpcUaPortNumber` | uint | Port (`OpcUaPort`). **[S]** | 4840 |
| `EnableStandardAddressSpace` | bool | Enable standard SIMATIC server interface (`OpcUaStandardServerInterface`). **[S]** | on |
| `MinimumPublishingInterval` | uint | Minimum publishing interval, ms (`OpcUaMinimumPublishingInterval`). **[S]** | 1000 |
| `MaxSessions`, `MaxSessionTimeout`, `MaxMonitoredItems`, `MaxRegisteredNodes` | uint | Maximum number of sessions, session timeout in s, monitored items, registered nodes (`OpcUaMaxSessions`, ...). **[S]** | 15, 30, 2000, 10000 |
| `OpcUaEnableGuestAuthentication` | bool | Enable guest authentication (`OpcUaGuestAuthentication`). **[S]** | on |
| `OpcUaEnablePasswordAuthentication` | bool | Enable user name and password authentication (`OpcUaPasswordAuthentication`). A switch, not a password, though named like one. **[S]** | off |
| `EnableSecurityPolicies_<suffix>` | bool | One per security policy, below (`OpcUaSecurityPolicies`). **[S]** | below |
| `AutoAcceptClient` | bool | Automatically accept client certificates during runtime (`OpcUaAutoAcceptAllClientCertificates`). **[S]** | on |
| `OpcUaEnableAlarmsAndConditions` | bool | Alarms and conditions (`OpcUaAlarmsAndConditionsEnable`). **[S]** | off |
| `SupportDeprecatedTypeDictionary` | bool | `OpcUaDeprecatedTypeDictionary`. **[S]** | on |
| `OpcUaEnableServerGlobalDiscoveryPush` | bool | `OpcUaServerGDSPushActive`. **[S]** | off |
| `OpcUaEnableDiagnosisClientRequests` | bool | One of the diagnostics switches (`OpcUaDiagnosticsClientRequestsFailedActive`); the others were not changed. **[S]** | off |
| `OpcUaSummarizeDiagnositcsInterval` | uint | `OpcUaDiagnosticsSummarizeInterval`, stored in ms where Openness gives s. Spelled so. **[S]** | 20 s |
| `ExportArrayMembers` | bool | Export all array elements as separate nodes (`OpcUaExportArrayMembers`). **[S]** | on |
| `ServerCertificateId`, `TrustedClientCertificatesIds`, `OpcUaServerManagedCertificatesGroupName` | string | References into the certificate manager, not certificates. The report says only whether one is assigned, and counts the trusted ones. | |

The minimum sampling interval (`OpcUaMinimumSamplingInterval`, default 1000 ms) is not on this item: it is the expando key `MinimumSamplingInterval` (uint, ms) of the PLC, the `S7ControllerTargetData`. **[S]**

**A key is stored only once its setting is changed.** A new CPU stores none of the keys above; switching the server on stores only `OpcUaEnableServer`, and every other setting still has its default. Changing the security policies stores the switch of every policy the firmware offers at once. So an absent key means TIA's default. The defaults in the table are what TIA Portal V21 sets for firmware V2.9, read through Openness; other firmware may differ. **[S]**

**Security policies.** Each suffix is the name of an Openness `OpcUaSecurityPolicies` flag without its prefix: setting exactly one flag stored that suffix as 1 and every other as 0. **[S]** The default flags are `None`, `256SHA_S` and `256SHA_SE`.

| Suffix | Openness flag | Policy |
|---|---|---|
| `None` | `OpcUaSecurityPoliciesNone` | No security |
| `128RSA_S`, `128RSA_SE` | `...128RSAS`, `...128RSASE` | Basic128Rsa15, sign; sign and encrypt |
| `256_S`, `256_SE` | `...256S`, `...256SE` | Basic256 |
| `256SHA_S`, `256SHA_SE` | `...256SHAS`, `...256SHASE` | Basic256Sha256 |
| `256RSA_OAEP_S`, `256RSA_OAEP_SE` | `...256RSAOAEPS`, `...256RSAOAEPSE` | Aes128Sha256RsaOaep |
| `256RSA_PSS_S`, `256RSA_PSS_SE` | `...256RSAPSSS`, `...256RSAPSSSE` | Aes256Sha256RsaPss |

Firmware V2.9 refuses the RSA-OAEP and RSA-PSS flags; they were set one by one on an S7-1500 with firmware V4.1 added to the copy. **[S]** The policy names follow the flags' names, and for the last two TIA's own messages, which call them Aes128Sha256RsaOaep and Aes256Sha256RsaPss. A sample that stores policy keys had its policies changed at some point, even if it now holds the defaults.

**The server storage** is a `Siemens.Simatic.OpcUa.Model.OpcUaServerConfigurationData`, subtype `OpcUa.ServerStorage`, with `Target` to its PLC. The V20 samples have one per PLC, the V18 sample none. Its expando values: **[S]**

| Key | Type | |
|---|---|---|
| `LastCompiled`, `LastDownloaded` | dateTime | Of the server interfaces. |
| `OPCUA.Expando.StructureJournalChanges`, `OPCUA.Expando.MappingJournalChanges` | string | A JSON-like list, `[]` when empty. That a user-defined server interface or companion specification would fill them is **[I]**; their filled form is **[?]**. |
| `UsersAndRolesStorage` | blob | The OPC UA roles, below. |
| `CompiledInterfaceBlob` | blob | A serialized `Siemens.Simatic.OpcUa.BusinessLogic.Services.CompiledServerInterface`: `LastCompiled`, `LastDownloaded`, `NextDownloadForced`, `RID`, `ASMBinary`, `DataMapping`, `ESData`, `RolesMap`, `Thumbprint`, `IsPrimaryInterface`. Not decoded further. |

The schema also has `OpcUaInterfaceData`, `OpcUaNodeData`, `OpcUaListData` and `OpcUaListContainerData`, presumably the objects of a server interface; no sample holds any. **[U]**

**Roles.** `UsersAndRolesStorage` is a 5 byte head (`01`, u32 0) and a .NET binary serialization stream ([MS-NRBF](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-nrbf/)) of a `Siemens.Simatic.OpcUa.BusinessLogic.UsersAndRolesStorageContainer`: `Roles`, a list of `UaRoleMappingInfo` (`RoleId`, `ProjectRoleName`, `RoleName`, `RoleComment`, `RolePermissions` as a list of `UaNamespacePermissionInfo` with `NamespaceIndex`, `NamespaceUri` and `Permissions`, `IsImportedRole`); `RolesMap`, from an id of the project's user management to node ids; `NodePermissions`; `AccessRestrictions` per namespace; `AccessRestrictionsMap`; `Version`. **[S]** `Permissions` are OPC UA's `PermissionType` bits (Browse 1, ReadRolePermissions 2, ..., Read 32, Write 64, ReceiveEvents 2048). **[I]** The stream holds no users and no passwords; users are in the project's user management (`Siemens.Automation.Umac...`), which this reader does not read.

**What a client may reach.** A data block is open to OPC UA unless `DBAccessibleFromOPCUA` is false (section 7.7). A member's access comes from the `MFlags` of section 11: a member that is not accessible is closed, one that is not writable is read-only, and a member under a closed or read-only one is the same; whether it is visible does not matter to OPC UA. **[S]**: TIA's node set export of the test data blocks has no node for a closed member or one under it, a read-only member's node has no write access, and a member that is not visible has a node like any other. A PLC tag (`EAMTZTagData`) has three flags in `Siemens.Automation.DomainServices.CommonTypeSystem.IStructureItem`, TIA's tag table columns: **[S]**

| Attribute | Read as |
|---|---|
| `HmiStructureTypeRelevant` | Accessible from HMI/OPC UA/Web API (Openness `ExternalAccessible`) |
| `HmiReadOnly` | not Writable from HMI/OPC UA/Web API (not `ExternalWritable`) |
| `HmiVisible` | Visible in HMI engineering (`ExternalVisible`) |

Four tags of the demo, set through Openness to not accessible, not writable, not visible, and visible again after not accessible, stored what this table says, and `tiadump opcua` reported each as TIA showed it. TIA ties the columns together: clearing Accessible clears Writable and Visible, and setting Visible sets Accessible again. So the tags of the samples with `HmiStructureTypeRelevant` clear and `HmiVisible` set hold a combination TIA V21 does not make; the reader reports them not accessible, and what TIA shows for them is **[?]**. A tag without an `IStructureItem` segment stores none of the three.

## 19. Hardware

Where a project's devices, subnets and IO systems are stored. Found by reading every object of the reference project `Hardware` (section 17), whose hardware TIA Portal V21 built through Openness from an invented description, and checked against every attribute TIA reports for the same project through Openness. `tiadump devices` lists it ([spec 0010](../specs/0010-hardware.md)). A claim marked **[S]** below was compared with TIA's report and agrees.

**Devices** are `Siemens.Simatic.HwConfiguration.Model.DeviceData` objects whose environment is the project: `ICoreAttributes.Name`, and `IDeviceData.InvariantTypeName`, TIA's type name (`S7-1500 station`, `ET 200SP-Station`). **[S]** A device in a device group has `AggregatingFolder` to the group's folder, and the path of folder names is the group path, as for a program folder (section 10); a device in no group has none. **[S]**

**Device items** are the objects whose type implements `...Model.IDeviceItemData`: `DeviceItemData`, `S7ControllerTargetData` for a CPU (the same object is the PLC, section 10), `HeadDeviceItemData` for an interface module, and `IODeviceModuleData`. `Parent` leads to the device for a rack and everything plugged into it, and to the device item it is built into for anything else: that is how TIA nests them. **[S]**

| Attribute | Holds |
|---|---|
| `ICoreAttributes.Name` | The name. An ET 200SP interface module's own submodule stores `_1`, or the module's type with `_1` (`IM 155-6 PN HF_1`, subtype `ET200SP.IM.Submodule`), and TIA names it as the module. **[S]** for both, on `Hardware` and `Safety`; that any `_<digits>` name works so is **[I]** |
| `IDeviceItemData.PositionNumber` | The slot: 0 a rack, 1 a CPU, 2 and up its modules; an interface 32768 and up, its ports after it. **[S]** |
| `OrderNumber`, `FwVersion`, `InvariantTypeName` | For a device item plugged into the device: its order number, firmware and type name, as TIA shows them. **[S]** Built-in items store some too (a CPU display's order number, a bus adapter port's firmware), and TIA shows none of them. **[S]** But an S7-1500 F-module's own submodule stores its type name, and TIA shows it: the reader shows a built-in item's type name when the item is no interface or port and its subtype extends its module's (`S71500.SM.DI.DI16` in `S71500.SM.DI`). **[S]** for the two F-modules of `Safety`, where a plain module's submodule stores none and an ET 200SP F-module's submodule, with its module's own subtype, shows none; the rule **[I]** An ET 200SP rack stores the placeholder `6ES5 710-8MAx1`, which TIA does not show; the reader leaves out an order number with a lowercase letter. **[S]** for this one, the rule **[I]** |
| `InvariantTypeName` of a built-in interface or port | Its label: `X1`, `X3`, `X1 P1`. **[S]** for interfaces. TIA's own label of a port comes from its catalog and can differ: `P1R` for a ring port. **[S]** An ET 200SP's interface stores none, where TIA shows `X1`. **[S]** |
| `DeviceItemType` | Bits: 0x2000 an interface, 0x4000 a port. **[I]** |

**What TIA does not list.** Of the stored device items TIA lists none whose subtype starts with `Internal.` (an interface's IO controller and IO device objects), nor a CPU's proxy, common part, execution unit, F-execution unit and licence manager (subtypes ending in `.CpuProxy`, `.CpuCommon`, `.CPUexecUnit`, `.CPUFexecUnit`, `.LicenseManager`), nor its virtual communication interface, whose node has `INodeData.NodeFeatures` 2 where an interface TIA lists has 4. **[S]** on a 1516-3 PN/DP and a 1511-1 PN, and the F-execution unit on the 1516F-3 PN/DP of `Safety`; an unlisted item's system constants are its parent's. Nothing stored marks them: TIA decides from its catalog, so for other CPUs the rule is **[I]**.

**I/O addresses** are dictionary expando values `AM_<n>_AddrData_<m>` (section 7.7) of the device item that has them, the submodule of a module. Each is the schema's structure `...Model.AddressItem` (section 7.4), after its u32 size: **[S]**, all 11 ranges of the reference project.

| Offset | Size | Element |
|---|---|---|
| 4 | u32 | `AdditionalInfo`: 2 inputs, 4 outputs |
| 8 | u16 | `AlarmObNumber` |
| 10 | u64 | `Length`, in bits |
| 18 | u32 | `ProcessImagePartNumber` |
| 22 | u64 | `StartAddress`, in bits: I 10 is 80 |
| 30 | u32 | `UniqueNumber` |

A device that is in no IO system has no address stored, where TIA reports start -1. **[S]** `AssignedAM_<n>`, an `ExpandoLink`, ties a range to an `AddressManagerNGData` **[I]**; other values of `AdditionalInfo`, and the `AddressMap` blobs of `AddressManagerV2Data`, are **[?]**.

**An interface's addresses** are a `NodeData` reached from the interface by `DeviceItemNodes`: `ICoreAttributes.Name` (`X1`, `IE1`, `PB1`), `INodeData.NodeType` (3 Ethernet, 1 PROFIBUS), and the relation `Subnet`. **[S]** Its dictionary expando values: **[S]**

| Key | Holds |
|---|---|
| `NodeIPAddress`, `NodeIPSubnetMask` | u32, the first byte the most significant: 3232235521 is 192.168.0.1 |
| `NodeIPConfiguration` | 0 the IP address is set in the project, 3 it is set directly at the device, and the address still stored is not the one used. Other values **[?]** |
| `PnNameOfStation` | The PROFINET device name as it was last generated or set |
| `PnPnNoSAutoGenerate` | 1: the device name is generated, and TIA generates it from the current names, in lowercase: for an interface of a CPU, the CPU's name and the interface's, `plc_locked.profinet interface_1`; else the interface module's, `remote_pn_2`. What is stored may be older: a PLC renamed after its CPU was created keeps `plc_1.profinet interface_1`. **[S]** on `Hardware` and `Safety` |
| `NodePBMpiAddress` | The PROFIBUS address |

**Subnets** are `SubnetData`: `ICoreAttributes.Name`, `ISubnetData.NetType` (3 Ethernet, 1 PROFIBUS), `SubnetNodes` to the nodes on it, `Mastersystems` to its IO systems. **[S]**

**IO systems** are `MastersystemData`: `ICoreAttributes.Name`; subtype `BusSystem.IOSystem` for PROFINET, `BusSystem.Mastersystem` for a DP master system; the expando value `PositionNumber`, the number; `Subnet`; `Master`, the IO controller: the interface's internal `IOController_PROFINET` item, whose parent is the interface, or the DP interface itself; and `HeadModules`, the IO devices: an `IODeviceModuleData` below the IO device's interface, or a DP device's interface module. **[S]**

**Hardware identifiers** are not attributes of the device items. A system constant holds each, a `SimaticConstantTagData` reached from the device item by `ConstantTags`: its name (`Local~DI_Main`), `IDefaultStrategyData.DefaultValue` the identifier, `IStructureItem.DisplayTypeName` its type (`Hw_SubModule`, `Hw_Interface`, `Hw_Device`, `Hw_DpSlave`, `Hw_IoSystem`), and `Target` the PLC whose tag table lists it; an IO device's constants are its IO controller's. **[S]** TIA reports a `Hw_Device` or `Hw_DpSlave` identifier on the device, a `Hw_IoSystem` one on the IO system, and the identifiers of the items it does not list on the nearest one it does. **[S]** It also reports identifiers that no constant holds (48 and 136 on each CPU), and puts the constant `Local~Configuration` on no object; the reader lists the constants, so only identifiers a constant holds. **[S]** Identifiers count per PLC: the same number means another thing on another PLC.

The CPU's common part also holds `EncryptedPassword*` and similar keys; the reader reads only the attributes named here, and `tiadump inspect` withholds those.

## 20. Watch and force tables

Read on the reference project `Tables` ([spec 0015](../specs/0015-tables-and-cross-references.md)), whose tables TIA V21 imported from SimaticML; every entry of its two watch tables and its force table agrees with TIA's export of them in every test run (section 17). **[S]** unless marked.

Objects of `Siemens.Simatic.Lang.Model.Debugging`:

| Type | Is |
|---|---|
| `VarTableData` | A watch table: `ICoreAttributes.Name`, in a `FolderData` by `AggregatingFolder` |
| `VarTableForceData` | The force table, one per PLC, named `Force table`, present and empty in a new PLC |
| `VarItemContainerData` | The table's lines, by `AggregatedVarItemContainer`; its simple expando (section 7.7) holds their comments |
| `VarItemData` | By `AggregatedVarItems` from the container: `IVarItemAttributes.ItemData` is the lines, as XML |

The PLC's folder of tables is stored as `WatchtablesFolder`; the folders below it are the user's. A table also has `IVarTableAttributeData.VarTableLayout`, the editor's columns, and an identifier table through `CoreObject2IdentContainer` that leads to the tags and data blocks its lines name; the reader needs neither.

`ItemData`, in the namespace `http://www.siemens.com/automation/2010/IECPL`:

```xml
<VarItems Version="14.0"><VarLines>
  <VarLine UId="23" Format="Dec_signed" RefId="2" Mute="False" IsCommentLine="False">
    <Address>%MW10</Address><Name>"Setpoint"</Name>
    <MonitorValue ValueUId="0" TriggerName="Smart" Checked="True" />
    <UserValue ValueUId="24" TriggerName="Smart" Checked="False" ValueRefId="3">10#50</UserValue>
  </VarLine>
  <VarLine UId="29" IsCommentLine="True" />
  <VarLine UId="40" />
</VarLines></VarItems>
```

- One `VarLine` per line in the editor's order. The last, with only a `UId`, is the empty line the editor offers for the next entry; it is no entry.
- `Name` is the operand as written, `"Recipe".limits.high` or `"Valve":P`; `Address` is its absolute address, stored for a tag and empty for a data block member. An entry on an address with no tag has an empty `Name`. TIA's export gives the name, or the address only when there is no name.
- `Format` is the display format. Stored names that differ from the export's: `Dec_signed` is `DEC_signed`, `Real` is `Float`; `Bool`, `Hex` and `Bin` are the same. `Dec_unsigned` and `Dec_sequence` are mapped the same way **[I]**; any other is passed through as stored **[U]**.
- `MonitorValue/@TriggerName` is the monitor trigger, `UserValue/@TriggerName` the modify trigger of a watch table. Stored `Smart` is the export's `Permanent`, `BeginOnce` is `OnceOnlyAtStart`, `EndCyclic` is `PermanentAtEnd`; `BeginCyclic`, `EndOnce`, `StopOnce` and `StopCyclic` are taken to be the other triggers by the same pattern **[I]**. A force table's value has `Now`, which the export does not give.
- `UserValue`'s text is the modify value, or the force value of a force table. A decimal value is stored with `10#` before it, which the export leaves out. `UserValue/@Checked` is the box beside the value **[I]**: TIA's import refuses to set it and its export does not give it, so every reference entry stores `False`.
- A comment line has `IsCommentLine="True"` and nothing else.
- Comments are not in the XML: the container's simple expando has one core text per commented line, under the line's `UId` written as a decimal number, the same form as interface member comments.

## 21. The cross-reference list: `XRef/XRef.db`

TIA's cross-reference list lies beside the database as a SQLite file ([sqlite.org/fileformat2](https://www.sqlite.org/fileformat2.html)), which the reader reads with a reader of that format of its own (ADR 0014): table b-trees, records and overflow pages, UTF-8 only, no indexes. Read on the thirteen reference projects, and compared on `Tables` with TIA's own list through Openness's `CrossReferenceService`: 169 rows agree in every test run, by user, object used, access and network (section 17). **[S]** unless marked.

**Does it belong to the database?** `XRef.db`'s table `info` holds `version` (10) and `savepointID`, a GUID. The database's `Siemens.Automation.ObjectFrame.CorePersistenceInfo` holds in `IPersistenceInfoAttributes.ProjectSavePoint` a string `XRef` followed by the same GUID as a string (section 7.2), beside one for `Vci`. The two agree in all thirteen reference archives and in two more the build wrote outside the repository: one saved after a block changed without a compile, and one after compiling it again. TIA rewrote the list in both, before any compile: the uses the change removed were gone. A list whose GUID differs from the database's is not the program's, and the reader takes the references from the text instead. The array's layout is not decoded; the reader finds the string `XRef` in the object's body and reads the string after it. **[I]**

| Table | Holds |
|---|---|
| `parts` | `binary`: `OG`, u32 type id, u64 object id, an object of the database. One row is `00`. |
| `objs` | `scopeObject` (the PLC), `primaryObject` (a part) and `subObject`: `00` for the object itself, else u32 member ids, a path into its interface |
| `rels` | One use: `left` uses `right` (both `objs`), with `leftType`, `rightType`, `location`, `rightFilter` and `relationInfo` |
| `addrs` | A tag's absolute address: `obj`, `type` (`0x1080`), `range` (the memory area) and `firstBit`, `lastBit`. Not read. **[I]** |

**Member ids.** A `subObject` id is the `ID` of a member in its block's interface XML (section 11), decimal there and u32 here: `ID="51"` is `33 00 00 00`. The next id names a member of that member's struct, UDT or FB type. A function's return value is named after the function, as TIA's list names it.

**Array indexes** are in `rightFilter`: `02`, a count, then per index a u16 segment of the path counted from 1 and an i32 index. `02 01 01 00 01 00 00 00` makes `steps.target` `steps[1].target`. One index on one segment was seen. **[I]**

**`relationInfo`**: the low half is 1 for a use, 2 for its inverse. The high half is the access, in TIA's terms:

| Code | Access | |
|---|---|---|
| `0x1`, `0x2`, `0x3` | Read, Write, RW | `RW` is an edge contact's or coil's memory bit |
| `0x6` | Declaration | a member declared of a UDT, from the interface |
| `0xC` | Call | of a block or a system instruction |
| `0xF` | Multiinstance | a member that is an FB or system instruction instance |
| `0x10` | InstanceDB | a data block of its own, as a call's instance; the inverse row leads from the data block to its FB |
| `0x5`, `0x8` | Definition, Jump | a label and a jump to it: not read |
| `0x21`, `0x4` with low half `0x80` | | among system blocks only: not read **[?]** |

**`location`**: u32 network or compile unit, counted from 1 (TIA's `NW` number in LAD, FBD and STL; 1 in SCL); u32 UId of the part or token in its stored form (section 13, section 12); then the right object's kind and, for a declaration, the member's id again. A network of 0 is the interface: the second number is the id of the member declared there. For GRAPH the unit is no network TIA names; its list says step or transition instead. **[I]**

**Kinds** in `leftType` and `rightType`: `0x1000` OB, `0x1010` FC, `0x1020` FB, `0x1060` DB, `0x1065` system instruction, `0x1080` tag, `0x10B0` UDT, `0x1100` label, `0x1110` data block member, `0x1120` interface member, `0x1125` UDT member. The reader goes by the part's object type instead. **[I]** A technology object (section 23) is used like a data block: the right object is its `TechnologicalDataBlockData`, and a member's ids are those of its interface, named as a data block's (`"Spindle".ActualSpeed`). **[S]** that the use is found, on `TechnologyObjects`; the member names **[I]**, since that project has no export of TIA's list

**What TIA derives.** TIA's list shows a call of an FB where the list holds only the use of its instance data block, and a call of an FB or system instruction where it holds only the use of a multi-instance member; the reader derives both the same way. A system instruction is an `IECPLInstructionProxyData`, named by its `ICoreAttributes.Author` (`TON`); TIA adds its version (`TON [V1.0]`). Some rows occur twice.

**Closed blocks.** The list holds a closed block's rows as an open block's, in clear: its calls, its reads and writes of tags, and of its own members with their token UIds. TIA's own list shows its uses of other objects with the place `?` and none of its own members. The reader shows the same (ADR 0015).

**Not in it**: watch and force tables. Neither the list nor TIA's `CrossReferenceService`, asked for a block, a data block or a tag, names one. The reader lists a table's entries as uses of what they name, with the access `Monitor`, and `Modify` or `Force` for an entry with a value: names of TIA's `Access` type, but no output of TIA's to compare with. **[I]**

## 22. Safety

Where a PLC's safety program keeps what TIA's safety administration shows: its settings, F-runtime groups, signatures, fail-safe blocks, and the F-parameters of the F-CPU and its F-I/O. Found by walking the objects of two samples with a safety program, then checked on the reference project `Safety` (section 17), whose build writes what TIA reports through Openness (`SafetyAdministration`, the blocks' `SafetySignatureProvider`, and the device items' `Failsafe_` attributes) beside its archive. A claim marked **[S]** is one the library's tests compare with that report. `tiadump safety` reports it all. This section names types and keys only.

**The safety program** of a PLC is a `Siemens.Simatic.Safety.Model.FailsafeInfo` whose `Target` is the PLC; a PLC without one has no safety program. **[S]** Its relations lead to the rest: `FailsafeInfoToFailsafeProgramSettings`, `FailsafeInfoToCompilationInfos`, `FailsafeInfoToRuntimegroupInfos` and `FailsafeInfoToFModuleInfos`. **[S]** The PLC also leads back to it through `ControllerTargetToFailsafeInfo`.

**Settings**, attributes of `IFailsafeProgramSettings` on the `FailsafeProgramSettings`: **[S]**

| Attribute | TIA's setting (Openness) |
|---|---|
| `SafetySystemVersion` | Safety system version, a string such as `V2.8` (`SafetySystemVersion`) |
| `ActivateFChangelog` | F-change history (`ActivationOfFChangeHistory`); absent means off |
| `SafetyModeCanBeDisabled` | Safety mode can be disabled; absent means no |
| `AutomaticOrManual` | `1` when F-block numbers come from fixed ranges (`ManagementMode` `FixedRange`); absent when the F-system manages them. A sample stores `-1`, which this reader also takes as managed. **[I]** for `-1` |
| `FunctionblockFrom`, `FunctionblockTo`, `FunctionsFrom`, `FunctionsTo`, `DatablockFrom`, `DatablockTo` | The ranges for F-FBs, F-FCs and F-DBs (`FromFB` ... `ToDB`), stored in either mode |
| `FProgramPasswordPlus` | The safety program password, as 64 characters derived from it, or empty without one. Only whether it is empty is read; Openness's `IsSafetyOfflineProgramPasswordSet` agrees in both cases |

The keys `FProgramPasswordChanged` (expando) and `PrivateCompilerSelectors` (on F-block information) stay unread, and the CPU's common device item holds `EncryptedPasswordFailsafe` and `PasswordConfirmationValid1` to `4`: `inspect` shows none of their values, and nothing else reads them.

**Signatures.** **[S]**

| Signature | Where |
|---|---|
| Collective F-signature (`CollectiveOfflineSignature`) | `IFailsafeCompilationInfo.OverallSignature` of the `FailsafeCompilationInfo`, a signed 32 bit integer to be read unsigned |
| Collective F-SW signature (`SoftwareOfflineSignature`) | Dictionary expando key `FunctionalSoftwareSignature` of the `FailsafeProgramSettings`, uint |
| Collective F-HW signature (`HardwareOfflineSignature`) | Key `FunctionalHardwareSignature`, uint |
| F-communication address signature (`CommunicationAddressOfflineSignature`) | Key `CommunicationAddressSignature`, uint |
| When it was compiled | `IFailsafeCompilationInfo.TimeLastGen`, .NET ticks in UTC (section 2) **[I]** for UTC |

Setting a safety password counts as a change: TIA then reports the collective and the F-SW signature as 0, and the archive holds no `OverallSignature` and `FunctionalSoftwareSignature` 0 until the program is compiled again. **[S]** The samples' compilation info holds no `OverallSignature` either, and their main safety block's `BodyCompileState` is `OutOfDate`, so their safety program was most likely changed after its last compile. **[I]** `OverallSystemSignature` beside it, and the F-runtime group's `OverallSignature`, are signatures TIA's administration does not show by those names. **[?]**

**F-runtime groups**, one `RuntimegroupInfo` each, numbered by `RuntimegroupID`: **[S]**

| What | Where |
|---|---|
| Name | `ICoreAttributes.Name` |
| Maximum cycle time, warning limit | `IRuntimegroupInfo.MaxCycleTime`, `WarningCycletime`, in µs (Openness `MaximumCycleTime`, `WarnCycleTime`) |
| Main safety block and its instance DB | Relations `RuntimegroupInfoToMainSafety`, `RuntimegroupInfoToMainSafetyInstanceDataBlocks` |
| F-OB | Relation `RuntimegroupInfoToMainSafetyCallers`. Its event class is the OB's subtype (section 10), `OB.CyclicInterrupt.Safety`, which TIA writes `Cyclic interrupt`; its `HandledEvent` is a `CyclicInterruptEventData` with `ICyclicInterruptEventData.CyclicTime` in µs (`FOBCycleTime`) and `IOrganizationBlockEventDataAttributes.Priority` (`FOBPriority`) |
| F-runtime group information DB | `IRuntimegroupInfo.Control1IDBSymbol`, its name (`InfoDbName`); relation `RuntimegroupInfoToControl1Idb` |
| Pre- and post-processing | `PreProcessorFCName`, `PostProcessorFCName`; empty in every object seen, as Openness's `PreProcessingName` and `PostProcessingName` are null. **[U]** when set |

**F-blocks.** A block is fail-safe when its relation `GeneralBlockSourceDataToBlockInfo` leads to an `FBlockInfo`; its F-signature is `IFBlockInfoExtension.FunctionalSignature` there, signed 32 bit, read unsigned, absent where Openness reports 0. **[S]** For all 77 such blocks of the reference project Openness reports exactly these blocks and signatures, the F-OBs, F-I/O data blocks and STEP 7 Safety's compiler blocks among them. TIA made a block when it sits in the system blocks folder (`SystemBlocksFolder`, below it `SafetyResourcesFolder` and `SafetyCompilerBlocksFolder`, `SafetyIODataBlocksFolder`), or is an F-runtime group's F-OB. **[S]** Most of those are know-how protected; their signature is in clear on the `FBlockInfo` all the same, and the report gives it while their body stays closed (section 15). `IFBlockInfo.IsHiddenFromUser` does not say which TIA made: it is set on the main safety blocks too. **[S]**

**The F-CPU's F-parameters** are dictionary expando keys of the PLC, the `S7ControllerTargetData`: **[S]**

| Key | TIA's setting (Openness attribute of the CPU) | Default |
|---|---|---|
| `Failsafe_CentralSourceAddress` | Central F-source address (`Failsafe_CentralFSourceAddress`) | TIA assigns one per F-CPU; stored on both of the reference project's |
| `Failsafe_DestinationAddress_LowerBound`, `Failsafe_DestinationAddress_UpperBound` | Range of F-destination addresses (`Failsafe_LowerBoundForFDestinationAddresses`, `...UpperBound...`) | as above: 1 to 99 for the first F-CPU of a project, 100 to 199 for the second **[U]** when absent |
| `Failsafe_Central_F_WD_Time` | Default F-monitoring time for central F-I/O, ms (`Failsafe_DefaultFmonitoringTimeCentral`) | absent means 150 **[S]** |

The CPU's `S71500.CPU.CPUFexecUnit` device item stores none of them.

**An F-I/O** is an `FModuleInfo` of the safety program; `FModuleInfoToDeviceItemData` leads to its device item, the module's submodule, whose `LogicalContainer` is the `DeviceData` of its device. Both names are what the device list of spec 0010 shows. **[S]**

| What | Where |
|---|---|
| First input and output address | `IFModuleInfo.InputBaseAddress`, `OutputBaseAddress`, bytes, `xs:uint` |
| F-monitoring time, ms | `IFModuleInfo.MonitoringTime`; the device item's key `Failsafe_F_WD_Time` holds the same |
| Set by hand | The device item's key `Failsafe_F_WD_Time_Setting` `1` (Openness `Failsafe_ManualAssignmentFMonitoringtime`) |
| F-source address | Key `Failsafe_F_Source_Add`, the F-CPU's central F-source address |
| F-destination address | Key `Failsafe_F_Dest_Add`; absent means 65534 |
| F-parameter signature with and without addresses | Keys `Failsafe_F_Par_CRC` (also `IFModuleInfo.ModuleCRC`) and `Failsafe_F_Par_CRC_WithoutAddresses` |
| F-I/O DB | `IFModuleInfo.IDBNr` and `StoredNameStorage` (Openness `Failsafe_FIODBNumber`, `Failsafe_FIODBName`) |

All **[S]**. The absent `Failsafe_F_Dest_Add` meaning 65534 rests on one module, the first TIA numbered; two F-I/O of a sample store none either. That TIA stores the key only once the address differs from 65534 is **[I]**. The device item's other `Failsafe_` keys are the channel parameters (sensor supply, discrepancy behaviour, short-circuit test and so on), which this reader does not report.

The schema also has `FCommunication`, `RuntimegroupCommunicationInfo`, `SafetyOverOpcUaProvider` and `SafetyOverOpcUaConsumer`, presumably F-communication between PLCs; no sample or reference project holds any. **[U]**

## 23. Technology objects

An axis, a PID controller, a counter: each technology object of a PLC is a data block of a technology type, stored as its own kind of object. Everything here was found on the reference project `TechnologyObjects` (section 17) and is checked in every test run against the values TIA reported through Openness when it built the project; **[S]** below means that check. `tiadump list`, `show` and `dump` write them, the dump as one `.to` file each.

| Full type name | Is |
|---|---|
| `Siemens.Simatic.Lang.Model.Blocks.TechnologicalDataBlockData` | A technology object. Not a `DataBlockData`: a reader that finds data blocks by that type misses them. **[S]** |
| `...Blocks.FunctionBlockTemplateData` | The type of a motion object, `TO_SpeedAxis` and so on. **[S]** |
| `...Blocks.CodeBlockData` | The type of the others, the instruction's FB, `PID_Compact` or `High_Speed_Counter`, closed. **[S]** |

**What it is.** Like a block: `ICoreAttributes.Name`, `IGeneralBlockSourceData.Number`, the folder by `AggregatingFolder` (TIA's own is named `TechnologicalParamFolder`, which TIA shows as *Technology objects*, with the user's folders below it), the PLC by `Target`. Its kind as TIA names it is the name of its type, reached by `GeneratedFromMaster` (motion) or `IsGeneratedFrom` (the others); its version is `IPlcHeaderData.HeaderVersion`. Both agree with Openness' `OfSystemLibElement` and `OfSystemLibVersion` on all 9 objects. **[S]** `IGeneralDataBlockSourceData.OfName` names the type otherwise (`PID_Compact_1500`), and `ICoreAttributes.Subtype` is a path of TIA's own, `DB.TO.SMC.TO_SpeedAxis.V6.0`, `DB.TO.PID.Compact.Compact_2.0`, `DB.TO.TM.HSC1500`. **[I]**

**Protection.** PID_Compact, PID_3Step, PID_Temp, High_Speed_Counter and SSI_Absolute_Encoder have `IsKnowHowProtected` and `Protection = SystemKnowHowProtection`, yet their interface and values are in clear; the axes have `NoProtection`. **[S]** The reader shows them (ADR 0016).

**The interface.** `CurrentInterface` leads to a root of kind `Values`, whose `UsedParts[0]` is the type's root, in clear: the sections `Input`, `Output`, `InOut`, `Static` with their members inline and an `<Offsets>` each, then the root's own `<Offsets retSize=".." volSize="..">`. A member of a structured type points by `SubPartIndex` at the type's part, a system UDT such as `TO_Struct_DynamicLimits` of kind `DataTypeSource`, followed as in section 11. A positioning or synchronous axis holds its base type as a member `Base` of an FB type (`TO_SpeedAxis`, whose own `Base` is `TO_Axis`). **[S]**

**Names.** TIA's name of a parameter is the member's path with `.`, without any `Base`, with an array element's index: `DynamicLimits.MaxVelocity`, `Sensor[1].ActiveHoming.HomePositionOffset`. Every parameter Openness reports that is not one of the settings below is such a path. **[S]**

**Values.** A value stored on the object is a `<V p=".." v=".."/>` in the root's payload, by id path as in section 11; TIA stores some there when it creates the object, not only what the user set. **[S]** Every other value is in the compiled image, `LoadableBinaries`, `LoadablePlusDataBlockData`, in one of two areas:

- A member whose own section member is marked `Remanence="Retain"` (a PID controller's `Mode`, `ErrorBits`, its `Retain` structure) is in `RetainBuffer`, the rest in `VolatileBuffer`. **[S]**
- Each member's offset counts within its area: the sum of the optimized offsets (`<o o="bits"/>`, in member order) along its path, from its section's `<Offsets>`, then each structure's and each type part's own. The offsets of the two areas overlap, so the area must be known. **[S]**
- An element of an array of a structured type is at the array's offset plus its index from the low bound times the type's `optSize`, from that part's own `<Offsets>`. **[S]**, on the arrays of four sensors of the positioning and synchronous axis.
- Each buffer is exactly its area, `volSize` or `retSize` bits, with no head, unlike the 8 bytes in front of a data block of a UDT (section 11). On all 7 compiled objects. **[S]** Values are little-endian; `Real` and `LReal` are IEEE. **[S]**
- An object TIA did not compile has both buffers empty. TIA refuses to compile an external encoder without its telegram and a counter without its hardware, and still archives the project; such an object has only the values stored on it. **[S]**, on two objects.

Of the 925 parameters with a value in the archive, 921 agree with Openness. The other 4 Openness gives in other units than the data block holds: a counter's `PulseDuration` in milliseconds against tenths of a millisecond, its `Measuring.UpdateTime` in milliseconds against microseconds. The reader shows what the data block holds. **[S]**

**Not in the data block.** Openness also reports settings whose names start with `_`, such as `_Units.LengthUnit`, `_Actor.Interface.Telegram` and the I/O addresses, and a counter's `Configuration.Module`, which is a device item. Where TIA stores them is not known. **[?]**

**Which members are parameters.** Openness reports a chosen list per kind: 36 of the speed axis's 143 leaf members, 86 of PID_Compact's 229. No stored mark gives that list: the set point bit `512` of `MFlags` (section 11) is on most of an axis's but on none of a counter's, and the list also holds a PID controller's inputs and outputs and a private internal member. **[S]** The reader therefore shows every member of the data block.

**Not checked.** Measuring inputs, output cams and cam tracks: Openness V21 neither creates nor imports them, so no reference project holds one; the reader reads them by the rules above. **[U]** Technology objects of S7-1200 CPUs, cams, kinematics and the interpreter: none seen. **[U]**

## 24. HMI

Where an HMI device's connections, tags, alarms and screens live, for both WinCC families: Comfort/Advanced (panels and WinCC RT Advanced, objects in `Siemens.Simatic.Hmi.Utah`) and Unified. Found with `tiadump inspect` on projects TIA Portal V21 built through Openness, then checked against the reference project `Hmi` ([spec 0011](../specs/0011-hmi.md)): a Unified MTP700 on an S7-1500, and the WinCC RT Advanced runtime of an ET 200SP Open Controller on its own software PLC. A claim marked **[S]** agrees with what TIA's Openness gave for every object of that project in every test run. TIA V21's Openness cannot create Comfort/Advanced alarms, a Comfort/Advanced HMI tag's link to a PLC tag, or a Comfort/Advanced screen item's tag binding, so those rest on the shared types below and on the two local samples (a V20 and a V21 archive of one project with a Comfort/Advanced device): **[I]**. `tiadump hmi` reports it all. This section names types and keys only.

**Families share their types.** Tags, tag tables, connections and alarms of both families are the same `Utah` types; only screens differ. The HMI application, `Siemens.Simatic.HwConfiguration.Hmi.Model.HmiApplicationData`, is every HMI object's `Target`; its `ICoreAttributes.Subtype` is `HMI.Application.RT.IL` for Unified and `HMI.Application.RT` for Comfort/Advanced, and `IHmiApplicationAttributes.DeviceApplicationType` names the device type (`MTP700 Unified Comfort`, `CPU 1515SP PC - WinCC RT Advanced`). **[S]** Its name is the runtime's name in TIA, not the device's.

| Object | Type (`Siemens.Simatic.Hmi.Utah.` unless named) | Read |
|---|---|---|
| Connection | `Communication.HmiConnectionData` | Name; `Subtype` `Integrated`; `IHmiConnectionAttributes.PlcId`, the driver. `ConnectionPoint` leads to a `HwConfiguration.Hmi.Model.HmiConnectionPointData`, whose `Conn2RemoteTargets` is the PLC's `S7ControllerTargetData`. **[S]** |
| Tag table | `Tag.HmiTagTableData` | Name; folder by `AggregatingFolder`, below the device's own root folder `HmiTagFolder`; `Content` lists its tags. **[S]** |
| HMI tag | `Tag.HmiTagData` | Name; `IStructureItem.DisplayTypeName`, the type, with a string's length (`WString[254]`) where TIA's API gives the length apart; `IHmiTagAttributes_Connection` to its connection, none for an internal tag; `IHmiTagAttributes_AcquisitionCycle` to an `Cycles.HmiCycleData`, whose name is the cycle (`T1s` on Unified, `1 s` on Comfort/Advanced). An absolute address is `ITagAddress.LogicalAddress` with `IHmiTagBaseAttributes.AddressAccessMode` `Absolute`. **[S]** |
| Member tag | `Tag.HmiStructureMemberTagData`, `Tag.HmiArrayMemberTagData` | The members of a tag of a structured type, under it by `Members` and `ArrayElements`, back by `MembersInverse` and `ArrayElementsInverse`; an array element is named `[n]`. The report lists the tag, not its members. **[I]** |
| Discrete alarm | `Alarm.HmiDiscreteAlarmData` | Name; `IHmiDiscreteAlarmAttributes.TriggerBitNumber`, absent for bit 0; `IHmiDiscreteAlarmAttributes_TriggerTag`; `HmiAlarmClassDataRelation` to an `Alarm.HmiAlarmClassData`; the event text in `ExpandoChildren`, an `Globalization.HmiTextData`, a core text whose text is TIA's rich text, `<body><p>...</p></body>`. **[S]** on Unified, **[I]** on Comfort/Advanced |
| Analog alarm | `Alarm.HmiAnalogAlarmData` | As above, with `IHmiAnalogAlarmLimitAttributes.LimitMode` (`UpperLimit`, `LowerLimit`, ...), `LimitValueConstant` as text, and `IHmiAnalogAlarmAttributes_TriggerTag`. **[S]** on Unified, **[I]** on Comfort/Advanced |
| Screen, Comfort/Advanced | `GraphX.HmiScreenData` | Name, `Subtype` `ScreenView` (templates, pop-ups and slide-ins have others), `IHmiScreenAttributes.Number`, `Width`, `Height`; `Children` lists its items. **[S]** |
| Screen item, Comfort/Advanced | `GraphX.HmiScreenItemData` | Name, `Subtype` (`TextField`, `IOField`, ...), `IHmiScreenItemAttributes.Left`, `Top`, `Width`, `Height`; an item of a group is in the group's `Children`. **[S]**. Its tags are `ExpandoReferences` to HMI and member tags. **[I]** |
| Screen, Unified | `Siemens.Simatic.Hmi.DL.ModernUI.ILScreenData` | Name; `ILScreenItems` lists its items. **[S]** |
| Screen item, Unified | `Siemens.Simatic.Hmi.DL.ModernUI.ILScreenItemData` | Name, `Subtype` (`HmiIOField`, `HmiText`, `HmiButton`); a bound tag is an `ExpandoReferences` to its HMI tag. **[S]** |

**Unified screens keep their properties as dictionary expando values under short keys**, a property's number written as characters: **[S]**

| Key | Type | On |
|---|---|---|
| `\u0005` | uint | Width of a screen or item |
| `\u0006` | uint | Height of a screen or item |
| `c` | int | Left of an item |
| `d` | int | Top of an item |
| `\u0007\u0005` | ushort | Number of a screen |

Other keys hold texts, layers and the bindings (`_#Dynamics`, a map); they are not decoded. **[?]**

**Alarm numbers.** Unified numbers its alarms across both kinds: `IHmiScadaAlarmAttributes.UniqueId`, which is what Openness calls the alarm's `Id`. `IAlarmData.AlarmId` counts per kind; Comfort/Advanced numbers by it. **[S]** for Unified, **[I]** for Comfort/Advanced.

**Drivers.** `PlcId` is an id from TIA's communication channel descriptions (`Data/Hwcn/Custom/CommunicationChannels` in TIA's installation), which also give the name TIA shows: `S7_1500_OMS` is `SIMATIC S7 1500`, `ILRT_S7_1500_OMS` `SIMATIC S7 1200/1500`. The reference project checks the second **[S]**; the reader's other names come from those files **[I]**.

**What an HMI tag reads in its PLC.** Each tag table that links tags to a PLC has a `Siemens.Automation.DomainModel.LinkMaintainer.ScopedLinkMaintainerData` (`Target` its HMI application, `InverseScopedLinkMaintainer` its table): `Consumer` lists the HMI tags, `Provider` the PLC tags (`EAMTZTagData`) and data blocks (`DataBlockData`) they read, each provider once however many tags read it. So the lists are not in step. **[S]** Which provider a tag reads comes from the tag itself:

- A tag linked to a PLC tag holds the PLC tag's address as its own `ITagAddress.LogicalAddress`, with `AddressMode` `Fixed`; it reads the provider with that address. **[S]** The reader takes the lists in step only where no address matches and both have the same length. **[I]**
- A tag linked to a data block member has the dictionary expando `TypeSafeAddress`, a structure (`0xE56001`) of `CRC` (uint), `LidPath` (an array of uint) and `Rid` (uint), stored as section 7.4 says: u32 size, `CRC`, a ref to the array, `Rid`. `Rid` is `0x8A0E` and the data block's number in its low 16 bits. **[S]** Each step of `LidPath` is a flag byte and 24 bits: `0x20` a struct, `0x40` a member that is not one, and the value its `LID` in the block's interface (section 11). **[S]** `0x10` with them marks an array index, whose value is the index as the source writes it. It counts from the array's lower bound as its declared type states it: element 3 of an `Array[1..5]` is stored as 2. **[S]** Index steps in a row are the dimensions of one array **[I]**; an empty path is the whole block, which the samples have **[I]**.
- Tags linked to PLC tags have a `TypeSafeAddress` too, with another `Rid` (`0x50` to `0x52` in the reference project, by memory area); it is not needed to find the tag. **[I]** `LinkHandle`, a structure of one `Index`, does not give the provider's position. **[I]**

**Runtime users** (`RTSecurity.HmiRTUserData`, `HmiRTGroupData`, `HmiRTAuthorizationData`) and the application's `Password` are not read; `tiadump inspect` withholds their values (spec 0004's rule). **[S]**

**Not read.** Recipes, text and graphic lists, scripts, logging and the HMI's runtime settings; a screen item's other properties, events and animations. TIA V21's Openness cannot create recipes for either family, so no reference project holds one. **[?]**

## 25. Program alarms, alarm classes and text lists

What the reference project `Texts` (section 17) holds of them, read from its archive and checked against TIA's own exports: its alarm instance texts and text lists as TIA's xlsx exports give them, and its alarm classes as TIA's alarm class export gives them ([spec 0014](../specs/0014-texts-and-alarms.md)). No sample has been read for them.

**A program alarm** is a `Siemens.Simatic.AlarmServices.ObjectFrame.Private.SimpleControllerAlarmData`. **[S]**

| | Alarm type | Instance alarm |
|---|---|---|
| `ICoreAttributes.Subtype` | `TypeAlarm` | `InstanceAlarm` |
| `ICoreAttributes.Name` | the `Program_Alarm` member that declares it | the same |
| `PhysicalParent` | the FB's `CodeBlockData` | the instance DB's `DataBlockData` |
| `InstanceAlarms` / `TypeAlarm` | each instance | its type |
| `AlarmClass` | its `AlarmClassData` | one only where it sets its own |
| `Texts` | 11 `AlarmText` objects | 11 of its own |
| `IAlarmData.AlarmId` | | the alarm's number, from 512 in `Texts` **[I]** |

The 11 `Texts`, in relation order: the info text, the alarm text, then additional texts 1 to 9. Each is a core text in `ICoreTextRepository.Text` (section 7.6). `ICoreAlarmTextData.Text0` to `Text10` are not stored. **[S]** An instance's text with no text in any language is its type's: TIA's export says *Inherited from Type* exactly there, and an instance with no class of its own has its type's class. **[S]** That a type's text with words in it is inherited the same way rests on this rule alone, since Openness sets only instance texts. **[I]** `IAlarmData.Priority` is -1 on every alarm of `Texts`, whose priorities were never set; what -1 means beside a set priority is **[?]**, so the reader shows no priority for it.

The type's dictionary expando `AlarmServices_AssociateValueInfo` is XML, `<AVIs><AVIL><AVI Index="0" Type="IecplVariable" IdentRef="4" />...`: each associated value by its index from 0 (`SD_1`), and the identifier (section 14) of the FB that is wired to it. **[S]**: these name `#level` and `#state`, the operands TIA's generated source of the FB wires to `SD_1` and `SD_2`. An instance's `AlarmServices_TextListBackReferences` names, per text and language, the text lists it uses; the reader takes them from the texts' `@n%t#Name@` placeholders instead, which gives the same. **[S]**

An alarm whose FB is know-how protected stays closed (section 15, ADR 0011), though its instance's texts are stored in clear and TIA's export shows them: the reader lists its name and instance and reads nothing more. **[S]**: none of those texts appears in what the reader returns.

**An alarm class** is an `AlarmClassData` of the project: `ICoreAttributes.Name`, `IAlarmClassData.Acknowledgement`, `GlobalId`, and its display name through `AlarmClassData_IDisplayNaming_DisplayName` to a `CoreText`, stored for LCID 65535 alone. Name, display name and id **[S]** against TIA's export, which is a zip named `.dat` holding XML. That `Acknowledgement` is the class's setting is **[I]**: the export has no column for it, and only the two built-in classes were read.

**A PLC text list** is a `Siemens.Automation.DomainModel.UserTextRangeListData`, with `Target` to its PLC: `ICoreAttributes.Name`, `IObjectRangeListData.ListRange` (`Decimal`, `Binary` or `Bit`) and a core text `ICoreAttributes.Comment`. `Entries` leads to each `UserTextRangeData`, with `IRangeData.RangeFrom`, `RangeTo` (absent is 0) and a core text `IRangeData.Text`. `IsUsedByAlarms` leads to each alarm whose text takes a text from it. **[S]**

**What the dump writes of them.** Per PLC of the project, beside its folders: `texts.tsv`, every title, comment, member and tag comment, text list entry, alarm text and alarm class display name, a column per project language with the text stored in it; `alarms.tsv`; and `textlists.tsv`. A tab, line break or backslash in a text is written `\t`, `\n`, `\r` or `\\`.

## Open questions

What is left of the comparison with TIA Portal, most important first. [Comparing with TIA Portal](comparing-with-tia.md) says how to run it.

1. **A second project.** Most of sections 11 and 12 was worked out from A alone, and A's V20 archive is the same program; `MFlags` has since been checked bit by bit on test blocks. B has not been compared with TIA. A rule that overfits shows there first.
2. **Fail-safe FBD in a real program.** The reference project `Safety` compares invented F-FBD and F-LAD networks with TIA's export (section 13), but all 48 FBD blocks of A are `F_FBD`, and TIA exports SimaticML only for a compiled block. Compiling A's fail-safe blocks needs permission for its safety program, which a script has no business looking for, so someone who has it logs in to the copy in TIA and compiles, as the comparison page says. Then compare them in statement form with TIA's export.
3. **`S7_SetPoint` on a fail-safe UDT.** Two global DBs of A have `MFlags="527"` on members of a fail-safe UDT, and TIA writes nothing where we write `'False'`. The rule may be wider than fail-safe types.
4. **LAD, FBD, STL and GRAPH in a real program.** The reference projects `Languages` and `MoreLanguages` check each on invented blocks (section 13), STL with indirect addressing and GRAPH step times among them, but no sample has any unprotected, so a real program's habits are untried: other instructions, nested branches, GRAPH with several sequences, GRAPH on a classic CPU. ProDiag supervisions on members are not read at all, for want of a way to build one (section 13).
5. **Hardware beyond the reference project.** Section 19 rests on one invented project of S7-1500 and ET 200SP devices: other families, GSD devices, HMI devices, a module whose inputs and outputs share one range, IP addresses from DHCP, and a real project's hardware are untried, and which device items TIA leaves out comes from its catalog, not the file. Module parameters are not read.
6. **Cross references in a real program.** The list is read and checked on invented blocks (section 21). Untried: whether a real archive's list always belongs to its database, the places of uses in GRAPH, arrays of several dimensions and accesses through pointers. The `addrs` table, TIA's overlapping accesses, is not read.
7. **HMI, Comfort/Advanced.** TIA V21's Openness builds no Comfort/Advanced alarm, link from an HMI tag to a PLC tag, or screen item bound to a tag, so these are read by the shared types alone (section 24). Someone with a WinCC Comfort licence could add them by hand to a copy of the reference project, archive it, and compare `tiadump hmi` with what TIA shows. Recipes, of either family, are not read at all.
8. **OPC UA.** The server's settings, all security policies, the tags' access columns and the members' access are checked (sections 11 and 18), and the node ids of TIA's node set export are in the [research note](research/2026-11-20-tia-opc-ua-server-settings.md). Left: what TIA shows for a sample's tag stored as not accessible but visible, a combination TIA does not let a person make; and a project with a user-defined server interface, for what the journals and `OpcUaInterfaceData` hold.

## This repository's reader

`src/TiaFormat` is built for reading a whole archive quickly: the file is one array, frames are value types, values are decoded on demand, blobs go into pooled memory, and SCL goes from UTF-8 tokens to UTF-8 text without strings. It reads frames, schemas, declared attributes, relations, simple expandos, and the booleans, integers, strings, dates and blobs of dictionary expandos. It does not decode structures, arrays or maps in general, because no block read needs them; it reads one structure, the hardware's I/O address (section 19), by its offsets, and the bytes of one structure value of a dictionary expando, an HMI tag's `TypeSafeAddress` (section 24). For the OPC UA roles (section 18) it has a reader of the .NET binary serialization format that only reads values and creates no type the stream names, and for the cross-reference list (section 21) a reader of the SQLite file format that reads tables and writes nothing.

```bash
dotnet run --project src/TiaDump -c Release -- dump <archive> <output folder>
```

`list` and `show` print blocks and technology objects, `show` also watch and force tables, `xref` lists cross references (section 21), `dump --raw` adds each network as stored, `opcua` reports section 18, `devices` section 19, `safety` section 22, `hmi` section 24, `languages`, `texts`, `alarms` and `textlists` section 25 and the project languages, and `inspect` prints what an object stores, its dictionary expando values marked `~`. A dump holds `<project or master copy>/<PLC>/<folder path>/<block>` with `.scl`, `.f_fbd`, `.udt`, `.db`, `.tags`, `.watch`, `.force` and `.to` files, `Project/<HMI device>/` with its connections, alarms, tag tables and screens as tab-separated files, and an `index.tsv` of every block and technology object with what stayed closed. The header and declarations are as a TIA source file has them, for reading and not for import. DB start values in our trailing comment come from the compiled image where the XML has none (section 11).

On the V20 samples the dump takes under a second. An independent implementation of this page, since retired, agreed with its text on every network body and interface member: 1,100 networks and 46,514 members. That checks the implementation against this page, not against TIA.
