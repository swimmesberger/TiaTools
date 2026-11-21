---
type: Plan
title: "Plan 0014: texts in every project language, program alarms and text lists"
description: "How spec 0014 is built: a probe of what Openness V21 takes, the Texts reference project and the exporter's languages, text lists and alarm texts, the reader's languages, texts table, alarms and text lists, and where the tests sit."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# Plan 0014: texts in every project language, program alarms and text lists

## Order

TIA Portal is at hand only for a while, so everything that needs it comes first; the reader is then written against the committed archive without TIA.

1. **Probe** (done, nothing committed): a throwaway project with three languages, an FB with two `Program_Alarm` multi-instances and its instance DB, a text list, and instance texts imported; its findings are below.
2. Write the sources of `tests/reference/Texts`, extend `TiaOpennessExport reference`, build (FR-019, FR-020). Commit archive and TIA's output.
3. Find where languages, alarms, alarm classes and text lists are stored, with `tiadump inspect`, and write it into `docs/FORMAT.md` (below), marked **[S]** as each test that could fail checks it.
4. Reader: languages and the chosen language (FR-001 to FR-005), then the texts table (FR-006, FR-007).
5. Text lists and alarm classes (FR-012 to FR-014), then program alarms (FR-008 to FR-011, FR-018).
6. `tiadump` commands and the dump's tables (FR-015), then the viewer (FR-016, FR-017).

## What is already known

From the demo project, read with a scratch walk of its object types:

- Languages: a `CoreCultureContainer` holds a `CoreCulture` (`ICoreCulture.LCID`, `.Name`) per language TIA knows; the project's are those with a `CultureIsUsedBy` relation to `ProjectData`, and `IsEditingCultureIn` and `IsReferenceCultureIn` mark the editing and the reference language. A global library has the same relations to its `LibraryData`. **[I]** until the reference project checks it.
- Every title and comment is a core text (section 7.6), whose string table already holds each language by LCID; `ReadCoreTextAt` reads them all and keeps one, preferring 1033 rather than the editing language. That preference changes; the decoding does not.
- Alarm classes are `AlarmClassData` objects of the project, with `IAlarmClassData.Acknowledgement`, `Priority`, `GlobalId`, and their display name as a `CoreText` through `AlarmClassData_IDisplayNaming_DisplayName`. The demo project has the two built-in classes only.
From the probe in TIA Portal V21 (step 1), through Openness and the probe's archives:

- Openness sets the project languages (`LanguageSettings.ActiveLanguages`, `EditingLanguage`, `ReferenceLanguage`) and a block's title and comment per language (`PlcBlock.Title.Items`, `.Comment.Items`); a change to them after compiling makes the block inconsistent, so they are set before the compile. SimaticML export lists a `MultilingualTextItem` for every project language, empty ones too.
- A program alarm is a `SimpleControllerAlarmData` of subtype `TypeAlarm` under the FB (`PhysicalParent` to its `CodeBlockData`), with `Name` the member, `AlarmClass`, `Priority`, `Texts` (11 `AlarmText` objects, each a core text in `ICoreTextRepository.Text`) and `InstanceAlarms`; each instance is one of subtype `InstanceAlarm` under the instance DB, with `TypeAlarm` back, its own `AlarmId`, `Texts`, and `AlarmClass` once set. The type's `~AlarmServices_AssociateValueInfo` is XML naming each associated value's identifier (`IdentRef`). The `ICoreAlarmTextData.Text0` to `Text10` attributes stay absent. The order of the 11 `AlarmText`s is not the xlsx's (the info text came before the alarm text), so `IAlarmData.TextIndexes` is the next thing to read.
- Core texts of alarms hold an extra entry for LCID 65535, empty; it is no project language and the reader ignores it.
- A PLC text list is a `UserTextRangeListData` (`Name`, `ListRange` Decimal, Binary or Bit, `Index`, a core text `Comment`) with `Entries` to `UserTextRangeData` (`RangeFrom`, `RangeTo`, core text `Text`), and `IsUsedByAlarms` to each alarm whose text uses it, which FR-014 can follow directly.
- Openness cannot create a text list or set an alarm's type texts. `PlcAlarmTextProvider.ImportInstanceTextsFromXlsx` sets instance texts and classes; a value equal to the type's is exported as `### Inherited from Type ###`. So the reference project's alarm types have empty texts, and an instance text inherited from a non-empty type text is not checked against TIA.
- The xlsx layouts TIA reads back: alarm instance texts exactly as `ExportInstanceTextsToXlsx` writes them (sheet `Instance alarms`, custom properties `TIA_Version` 2.1 and `FileContent` `Alarm instances`, columns `Location`, `Alarm name`, `Alarm class`, then per text one column per language and a `FieldInfo` column); text lists in sheets `TextList` (`Name`, `ListRange`, `Comment [<culture>]`) and `TextListEntry` (`Parent`, `From`, `To`, `Text [<culture>]`), with `FileVersion` 1 and `FileContent` `Alarm text lists`. TIA's import needs a styles part; without one it crashes.
- `AlarmClassDataProvider.Export` takes only a `.dat` file.

## The reference project

`tests/reference/Texts/sources/`, in the layout of spec 0008, plus:

- `languages.txt`: one line per project language, `editing de-DE`, `reference en-US`, `active fr-FR`. German as editing language checks that the reader follows the project, not English.
- `PLC_Texts/PLC tags/Signals.tsv`: as before, name, type and address, then the comment as `culture=text` fields, one tag with English only.
- `PLC_Texts/Program blocks/`: `Tank.scl`, an FB in SCL with two `Program_Alarm` multi-instances, `SD_1` and `SD_2` wired, member comments in German alone, and its instance DBs `TankA` and `TankB`; `block texts.tsv` gives Tank's title and comment in all three languages, set through Openness before the compile. `Valve.xml`, an FB in LAD in SimaticML, has a block title and comment, three networks and four members whose titles and comments are in all three languages or left empty in some, so that both steps of the fallback are needed. `Recipe.xml`, a global DB in SimaticML, has member comments in three languages and in English alone. `Closed/Sealed.scl` with its instance DB `SealedA` raises an alarm and is listed in `protect.txt`. The block texts are invented and the folder names follow spec 0008.
- `PLC_Texts/PLC text lists/<name>.tsv`: `ListRange`, `Comment`, then from, to and a text per culture for each entry: `States`, decimal, with single values and a range, which `Tank`'s low alarm uses, and `Doors`, a bit list no alarm uses, with one text missing in German.
- `PLC_Texts/PLC alarms/alarms.tsv`: per alarm instance, its instance DB, member, class, and alarm text, info text and additional text 1 per culture, with `@1%d@` and `@2%t#States@`; `SealedA`'s alarm has texts that must never appear in the reader's output. `TankB` keeps the types' empty texts.
- The exporter turns the text list and alarm tables into the xlsx layouts step 1 found, in a temporary folder, with `System.IO.Compression`; the sources stay text, so a review reads them.

## Exporter

`tools/TiaOpennessExport/Program.cs`:

- `Reference` reads `languages.txt` before the PLCs: adds each language to `LanguageSettings.ActiveLanguages`, sets `EditingLanguage` and `ReferenceLanguage`.
- `BuildPlc` writes tag comments per culture through `MultilingualText.Items`; after the blocks, imports the text lists with `PlcAlarmTextListProvider.ImportFromXlsx`, compiles, imports the alarm texts with `PlcAlarmTextProvider.ImportInstanceTextsFromXlsx`, and compiles again. A result that is not a success counts as a refused source (FR-020, as spec 0008's FR-009).
- `Export` writes, beside the generated source: `languages.txt` from `LanguageSettings`; SimaticML of every block of a project with `languages.txt` (not only those TIA generates no source for), because only SimaticML lists every culture's text; `textlists.xlsx` from `ExportToXlsx`; `alarms.xlsx` from `ExportInstanceTextsToXlsx(..., All)`; and the alarm classes from `AlarmClassDataProvider.Export` as `alarmclasses.dat`. `manifest.tsv` lists the new files under their kind.
- The other reference projects have no `languages.txt` and build as before.

## Reader

All in `src/TiaFormat`, base class library only.

- `TiaDatabase.ReadCoreTexts` gives every language of a core text as a list of `(int Lcid, string Text)`; `ReadCoreText` takes a preference order of LCIDs instead of preferring 1033, with the fallback of FR-004.
- `TiaLanguages` (new, `Program`): a project's or library's languages, editing and reference language, read from the culture relations; resolves a culture name for FR-005.
- `TiaProgram` takes the language to write in, by default the editing language, and passes its preference order to every title and comment it reads; `TiaSourceWriter` is unchanged, so FR-003 holds for the dump, `show` and the viewer alike.
- `TiaProgram.BlockTexts` and `TagTexts` give a block's and a tag table's titles and comments in every language, by place: `title`, `network 2 comment`, `member level` (member comments are stored by id path and named through the interface).
- `TiaAlarms` (new) reads alarm types and instances, alarm classes and PLC text lists as the format page's section 19 says. An alarm's associated values come from its type's `AlarmServices_AssociateValueInfo`, which names the FB's identifier wired to each `SD_n`, rather than from the call in the networks (FR-010). Text list uses are found in the stored alarm texts by their `%t#name` placeholders (FR-011); a list's users through its `IsUsedByAlarms` relation (FR-014). An alarm whose declaring FB is know-how protected is listed with its name, instance and `closed`, and nothing more (FR-018).
- `TiaTexts` (new) makes the texts table's rows, writes the three tables tab-separated with a column per project language, and shows the alarms and text lists as aligned tables in one language for the viewer. A cell holds the language's own text, else the language-neutral (LCID 65535) one, never another language's.
- `TiaSourceExport` writes, per PLC of the project, `texts.tsv`, and `alarms.tsv` and `textlists.tsv` where there are any, next to its folders; `index.tsv` stays a list of blocks. A `--grep` dump writes none.
- `TiaProject.Open` takes the language, and adds an Alarms and a Text lists item per PLC that has them, beside the OPC UA report: searched, shown as a document, not exported on their own.

## Tool and viewer

- `tiadump languages`, `texts`, `alarms` and `textlists <archive> [--plc]`; `--language <culture>` on `show` and `dump`. The tables carry every language, so they take no language.
- Viewer: View, Language lists the project languages, the one shown checked (`ViewerLanguages`); choosing another opens the file again in it, at the item that was shown. The Alarms and Text lists items show their table as text in the chosen language, so search, copy and the editor work on them as on a block.

## Tests

In `TiaTextsTest`, on the reference project Texts, one test per acceptance scenario and FR:

- `EveryBlockMatchesTiasGeneratedSource` runs on Texts like on every reference project (FR-022): its SCL FB and DBs in German, the editing language; the global DB `Recipe` is known, as the format page's section 12 says.
- `EveryTitleAndCommentInEveryLanguageMatchesTiasExport` (FR-021): every block TIA exported as SimaticML, each `MultilingualText` of block and network and each member comment, per language, and each tag comment per language, against the reader's texts. The network and tag comparisons of spec 0008 and 0009 read TIA's side by the rule of FR-004 (`SimaticMlTexts`).
- `EveryTextListEntryInEveryLanguageMatchesTiasExport`, `EveryAlarmTextInEveryLanguageMatchesTiasExport` and `EveryAlarmClassMatchesTiasExport` read TIA's xlsx with a reader in the test project (`XlsxSheets`: `ZipArchive` and the sheets' XML, no package), and the alarm classes' `.dat`.
- `TheLanguagesMatchTias`, the fallback scenarios, the unknown language, the texts table, the alarm, alarm class and text list scenarios, and `NoTextOfAProtectedBlocksAlarmAppearsInTheDumpTheTablesTheSearchOrTheViewer` (FR-018).
- The rules are tested on the reference project instead of on files `TiaFileBuilder` writes: it holds every case of FR-004, and was built by TIA.
- `TiaViewer.Tests`: the language list offers the project languages, editing and reference marked, the one shown checked.
- Each comparison was seen to fail on purpose: dropping block titles, swapping the alarm and info text, and leaving a protected FB's alarm open each fail them.

## Format page

`docs/FORMAT.md` gains a section on texts and languages (cultures, editing and reference language, the fallback), one on program alarms and alarm classes, and one on text lists, each with **[S]** only where the reference project's comparison checks it. Section 12's sentence on the editing language is corrected to say what the reader does. Section 17 lists the Texts reference project.

## Risks

- Openness sets only instance texts (step 1), so how an instance inherits a non-empty type text rests on the archive alone, marked **[I]** on the format page until a project with type texts confirms it.
- The alarm class export's `.dat` is a zip holding XML of each class's name, display name and id, but not whether it needs acknowledgement; that stays **[I]** on the format page.
- SimaticML import may refuse texts in a language that is not yet active; the languages are set before any block is imported.

## Decisions

- ADR 0011 (protected stays closed), ADR 0012 (reference projects). No new ADR: the dump's new tables and the language option add output and change none that is relied on, except that a project whose editing language is not English now dumps in that language, which section 12 already claimed it did.
