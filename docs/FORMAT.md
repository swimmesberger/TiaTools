---
type: Format reference
title: "The TIA Portal project database format"
description: "The file format of TIA Portal projects, archives and libraries, enough to write a read-only reader in any language, with how far real archives confirm each claim."
status: stable
---
# The TIA Portal project database format

Enough to write a read-only reader in any language: container, frames, schema, values, relations, then the PLC block layer (interface, SCL, FBD/LAD networks, identifiers). `src/TiaFormat` implements it, and `src/TiaDump` dumps an archive's program as source files with it.

Checked on four files: two V20 archives (`.zap20`, called A and B here, 58 and 89 MB), A upgraded by TIA V21 (`.zap21`, 59 MB) and one V15.1 global library (`.al15_1`, 4 MB). The OPC UA configuration (section 18) was checked on three more: two V20 archives, C and D, and one V18 archive, E. Siemens publishes no description of this format, so it can change with a TIA version.

## How to read the claims

| Mark | Meaning |
|---|---|
| **[S]** | Confirmed on the samples by a check that could have failed. Section 17 lists the numbers. |
| **[U]** | Untested: the samples never exercise it, or it was not checked. Treat it as a lead. |
| **[I]** | Inferred from the samples, with no check that could have refuted it. |
| **[?]** | Unknown. |

**The generated text of SCL blocks, data blocks and UDTs has been compared with TIA Portal,** through Openness and for every unprotected block of one project: section 17 has the result. FBD networks have only been compared by eye, on two blocks. Everything stored but not rendered has not been compared at all.

## 1. The containers

- `.zap<NN>` (`.zap20`): a ZIP of a project folder. **[S]**
- `.al<NN>` (`.al15_1`): a global library, already an unpacked folder. **[S]**

| Path | Content |
|---|---|
| `System/PEData.plf` | The database: this document. |
| `System/PEData.idx` | Index over it (section 9). Not needed to read the file; useful as a check. |
| `*.ap20`, `*.al15_1` at the top | UTF-8 XML: `Type`, `Name`, `ProjectCompatibilityVersion`/`ProjectVersion`, an icon. |
| `XRef/XRef.db` | SQLite cross references: tables `parts`, `objs`, `rels`, `addrs`. Their binary columns are not decoded. **[?]** |
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

String table: u32 `n`; `n`×u16 language ids (LCID: 1033, 1031, ...); `n`×u32 present flags; for each present one, u32 length and UTF-8 bytes. **[S]**

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
| `...Blocks.IGeneralBlockSourceData` | `BlockType` (FB, FC, ...), `Number`, `BlockLanguage` (SCL, F_FBD, DB, ...), `OnlySymbolicAccess`, which a source file shows as `S7_Optimized_Access` | `FALSE` agrees with TIA on one block **[S]**; `TRUE` **[U]** |
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

`01` Bool, `02` Byte, `03` Char, `04` Word, `05` Int, `06` DWord, `07` DInt, `08` Real, `09` Date, `0A` Time_Of_Day, `0B` Time, `0C` S5Time, `0E` Date_And_Time, `13` String, `16` Any, `1D` Timer, `1F` IEC_Timer, `30` LReal, `31` ULInt, `32` LInt, `33` LWord, `34` USInt, `35` UInt, `36` UDInt, `37` SInt, `3C` ARef, `3E` WString, `3F` Variant, `40` LTime, `41` LTOD, `42` LDT, `43` DTL, `88` TYPE_ID, `90` HW_ANY, `91` HW_IOSYSTEM, `97` HW_SUBMODULE, `9B` HW_INTERFACE, `AA` CONN_OUC, `D0` DB_ANY. `02000000` never appears in such a table; it is on `Ret_Val` of an FC without a return value, so Void **[I]**. `A0` is in none either: only an OB's `Csg_OBNr` input uses it, and `D0` its `DBNr`. TIA's export of such an OB calls them OB_ANY and DB_ANY. **[S]**

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

TIA writes titles and comments in one language, the project's editing language. Where that one is empty the reader takes the first language that has text, so as not to lose it, and differs from TIA on purpose. **[S]**

A UDT is `TYPE`, `   STRUCT`, its members, `   END_STRUCT;`, a blank line, `END_TYPE`, with no `S7_Optimized_Access`. **[S]**

## 13. FBD and LAD networks

Each `CompileUnitData` is one network, with `<FlgNet xmlns="http://www.siemens.com/automation/2015/FunctionLadderDiagram" Version="14.0.0.2" Lang="F_FBD">`. Network title: `ICoreAttributes.Comment` of the compile unit. **[S]** Languages in the samples: `F_FBD`, `FBD_CLASSIC`. **LAD, plain FBD, STL and GRAPH source occur in no unprotected block here, so their forms are unknown.** **[?]**

```xml
<Parts>
  <ORef UId="24" RefId="1"/>             <!-- an operand: RefId into the identifiers; none means not connected -->
  <Part UId="23" Gate="Coil"><Negated PinName="in"/><TemplateValue .../></Part> <!-- a gate -->
  <LRef UId="21" RefId="5"><Instance UId="22" RefId="4"/></LRef>                <!-- a library call -->
  <CRef ...><CodeBlock RefId=".."/></CRef>                                       <!-- a block call -->
</Parts>
<Wires>
  <Wire UId="28"><OCon UId="24"/><PCon UId="21" PinName="IN"/></Wire> <!-- first end is the source -->
</Wires>
```

The first connection of a `Wire` is its source; the others are sinks. **[I]** `Negated` names a pin of its part, an input or an output, so an inverted output inverts what its wire carries. `OCon` is an operand, `PCon` a part's pin. Gates in the samples: `Coil`, `A`, `O`, `Move`, `Le`, `Ge`, `Ne`, `Sr`, `Rs`, `Abs`, `Sub`, `Return`, `Jump`, `JumpNot`, `NBox`, `PContact`. A network has no text form of its own: `FlgNetRenderer` prints calls, one per part, which is our layout and not TIA's. `Labels` holds jump labels.

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

A path segment that is not a plain identifier is quoted. `BPIL/BPI` gives a called block's parameters with `S`ection and type, `TD/@T` a type (section 11).

Note that **this table repeats names used in the block**, which is why a plain text search over a whole archive overcounts source occurrences (section 17).

## 15. Know-how protection and what stays closed

Stays closed, by choice: this reader does not attempt it and does not need it.

- `Data` of a protected block starts `<EncryptedData Type="http://...`: report it and stop.
- Still in clear on a protected block in the samples: name, kind, number, language, folder and `IsKnowHowProtected`. Some also have an interface and comments in clear: 93 of 607 protected block objects in A, 22 of 447 in B. The rest, Siemens system blocks among them, have none stored.
- Their identifier parts are in clear (209 in A, 88 in B). The reader takes only type codes from them for its table and never renders them.
- `ICoreAttributes.Protection` is a separate attribute. Use `IsKnowHowProtected` for the flag.

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

The text is the same after compiling. TIA exports SimaticML only for a compiled block, so a retrieved copy was compiled block by block, with every block that is not fail-safe, and closed unsaved. It refuses to compile the whole PLC without permission for the safety program. That gave SimaticML for all 319 UDTs, 85 of 92 open DBs, 194 of 379 open SCL blocks, and the same table as above.

**What this does not cover.** FBD networks: all 48 in the project are `F_FBD`, so they stay uncompiled and TIA exports none of them. Protected blocks, by choice. LAD, plain FBD, STL and GRAPH: none in the project are open.

**Values still not decoded, by design or not understood**: `0x80000016` in 6 HMI objects of B; 24 keys unknown in an HMI `StyleData` simple expando; `XRef.db`; `MetaId`; `0x70003` to `0x70006`. In the V15.1 library 12 `DataBlockData` objects do not fit the schema for some attributes (`Subtype`, `Culture`, `Password`, `Version`, `ITagAddress`), whichever of its three schemas is used: an offset leads out of the frame. A reader must treat such a value as absent. **[?]**

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
| `256RSA_PSS_SE`, and presumably `256RSA_PSS_S`, `256RSA_OAEP_S`, `256RSA_OAEP_SE` | `...256RSAPSSSE`, `...256RSAPSSS`, `...256RSAOAEPS`, `...256RSAOAEPSE` | Aes256Sha256RsaPss, Aes128Sha256RsaOaep **[I]** |

The policy names follow the flags' names. Firmware V2.9 refuses the RSA-PSS and RSA-OAEP flags, and a CPU with newer firmware could not be added without a STEP 7 Professional licence, so those keys and names stay **[I]**. A sample that stores policy keys had its policies changed at some point, even if it now holds the defaults.

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

## Open questions

What is left of the comparison with TIA Portal, most important first. [Comparing with TIA Portal](comparing-with-tia.md) says how to run it.

1. **A second project.** Most of sections 11 and 12 was worked out from A alone, and A's V20 archive is the same program; `MFlags` has since been checked bit by bit on test blocks. B has not been compared with TIA. A rule that overfits shows there first.
2. **FBD.** All 48 FBD blocks of A are `F_FBD`, and TIA exports SimaticML only for a compiled block. Compiling fail-safe blocks needs permission for the safety program, which a script has no business looking for, so someone who has it logs in to the copy in TIA and compiles, as the comparison page says. Then compare parts, wires and negated pins as data with what `FlgNetRenderer` reads, not as text, since our FBD text is our own layout. That would also settle whether a network stores `Time` under `TON`, as TIA shows it, where we print `TON`.
3. **`S7_SetPoint` on a fail-safe UDT.** Two global DBs of A have `MFlags="527"` on members of a fail-safe UDT, and TIA writes nothing where we write `'False'`. The rule may be wider than fail-safe types.
4. **LAD, plain FBD, STL and GRAPH.** No sample has any unprotected. A small new project in TIA with one block of each would close that, and being invented, like the demo project, it can go into the tests as a fixture. It would also test the rules on something new.
5. **Hardware, optional.** The reader lists no devices. A `tiadump devices` command (name, type, address, network) could be checked against TIA's Network overview.
6. **OPC UA.** The server's settings and the tags' access columns are checked (section 18). Left: the RSA-PSS and RSA-OAEP policy keys on a CPU with firmware V3 or later, which needs a STEP 7 Professional licence to add; what TIA shows for a sample's tag stored as not accessible but visible; a data block member's access (`MFlags`) in TIA's block editor, which needs the licence to generate a test block; the node ids and folders of the address space (spec 0006), from TIA's *OPC UA › Server › Export* or a client on a running server; and a project with a user-defined server interface, for what the journals and `OpcUaInterfaceData` hold.

## This repository's reader

`src/TiaFormat` is built for reading a whole archive quickly: the file is one array, frames are value types, values are decoded on demand, blobs go into pooled memory, and SCL goes from UTF-8 tokens to UTF-8 text without strings. It reads frames, schemas, declared attributes, relations, simple expandos, and the booleans, integers, strings, dates and blobs of dictionary expandos. It does not decode structures, arrays or maps in general, because no block read needs them. For the OPC UA roles (section 18) it has a reader of the .NET binary serialization format that only reads values and creates no type the stream names.

```bash
dotnet run --project src/TiaDump -c Release -- dump <archive> <output folder>
```

`list` and `show` print blocks, `dump --raw` adds each network as stored, `opcua` reports section 18, and `inspect` prints what an object stores, its dictionary expando values marked `~`. A dump holds `<project or master copy>/<PLC>/<folder path>/<block>` with `.scl`, `.f_fbd`, `.udt`, `.db` and `.tags` files, and an `index.tsv` of every block with what stayed closed. The header and declarations are as a TIA source file has them, for reading and not for import. DB start values in our trailing comment come from the compiled image where the XML has none (section 11).

On the V20 samples the dump takes under a second. An independent implementation of this page, since retired, agreed with its text on every network body and interface member: 1,100 networks and 46,514 members. That checks the implementation against this page, not against TIA.
