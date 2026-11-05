# TiaTools

A read-only reader for Siemens TIA Portal archives, without TIA Portal.

| | |
|---|---|
| `src/TiaFormat` | The library: frames, schema, relations, blocks, SCL and FBD source. .NET 10, base class library only |
| `src/TiaDump` | `tiadump`, the command line tool built on it |
| `tests/TiaFormat.Tests` | Unit tests; they write their own small files, so they need no real archive |
| [docs/FORMAT.md](docs/FORMAT.md) | The format, enough to write a reader in any language. Each claim says how far real archives confirm it |

## Use

```bash
dotnet run --project src/TiaDump -c Release -- list <archive>
```

```bash
dotnet run --project src/TiaDump -c Release -- show <archive> <block>
```

```bash
dotnet run --project src/TiaDump -c Release -- dump <archive> <output folder> --verify
```

An archive is a `.zap*`, a `.al*` or a `PEData.plf`. A dump holds `<project or master copy>/<PLC>/<folder path>/<block>` with `.scl`, `.f_fbd`, `.udt`, `.db` and `.tags` files, and an `index.tsv` of every block.

Know-how protected source stays closed: the reader reports it and does not try to open it.

Archives and dumps hold the code of whoever owns the project, so they never go into this repository.

## Build and test

```bash
dotnet build TiaTools.slnx
```

```bash
dotnet test --project tests/TiaFormat.Tests
```
