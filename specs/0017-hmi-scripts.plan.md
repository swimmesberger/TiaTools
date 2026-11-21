---
type: Plan
title: "Plan 0017: HMI scripts, custom web controls and screen previews"
description: "How spec 0017 is built: what TIA V21 holds and builds for HMI code, the reference project, the reader, tiadump and the dump, and the viewer's source view, screen preview and web view."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-21T18:00:00Z }
status: stable
---
# Plan 0017: HMI scripts, custom web controls and screen previews

## Order

TIA Portal V21 is a trial here, so the reference project comes first; the reader is then written against the committed archive.

1. Probe what TIA V21 stores for each kind of HMI code, and what its Openness builds: done, below. Nothing left: the reference project's build places its custom web control by hand, the rest by script.
2. A spike: publish the viewer with Native AOT with `Avalonia.Controls.WebView` in it, showing a local page. Done on Windows: it works without warnings, given an application manifest (ADR 0017, The Windows spike). Left: macOS, which accepts or changes ADR 0017 before any viewer code is written.
3. Extend the exporter and build the reference project `HmiScripts`.
4. The reader, `tiadump hmi --scripts` and the dump.
5. The viewer: source view, screen preview, custom web control preview.
6. `docs/FORMAT.md`, `docs/releasing.md` for the new dependency, glossary, log.

## What the probe found

On the probe project in `C:/Users/Simon/tr-hmi`, built and archived through Openness by TIA V21:

- **Unified event, property and screen scripts build through Openness** (`EventHandlers.Create(type).Script.ScriptCode`, `Dynamizations.Create<ScriptDynamization>(property)`), and read back. Each screen has one `DL.Scripting.HmiScriptModuleContainerData` (`Scope` `Screen`, `ScriptContainerParent` the screen) whose `EventElements` are event scripts and `TriggerElements` property scripts, each an `HmiScriptEmbeddedData`: `Name` (`OnTapped`, `BackColorTrigger`), `IScriptAttributes.Language` `JavaScript`, `Scope` `Screen.Event` or `Screen.Trigger`, `EmbeddedScriptParent` the screen item.
- **The code is a `CodePartList`**: an array (u32 size, u32 count) of refs to `CodePart` structures (`Data` string, `IsReference` bool), the code token by token: `if`, ` `, `(`, `Tags`, `(`, `"`, a reference, `"`, ... A reference's `Data` is a number standing for an HMI tag or screen item (`Tags("Speed")` holds one); what the number maps to is still to find.
- **Global script modules build through Openness** once the layout is known, which one made in TIA's window and exported showed: `Scripts.Import(folder)` takes per module a `<module>.hmi.yml` (`#Version: 2.0`, then `ScriptModules:` → module name → `ScriptFile: <module>.hmi.js`) and that `.hmi.js`, the global definition area first, ended by an auto-generated comment, then the module's functions. Stored as `DL.Scripting.HmiScriptGlobalModuleData` (`Name`, `IScriptModuleAttributes.Scope` `Global`) with a `GlobalSection` (`HmiScriptGlobalSectionData`, "Global definition area") and per exported function an `HmiScriptGlobalData` (`Name` the function's, `IsExported`), each with its own `Code`: TIA splits a module into its functions, so the reader joins them again.
- **VB scripts build through Openness** by SimaticML import (`Hmi.RuntimeScripting.VBScript` with `Name`, `Code`, `PreCode`, `Type`). Stored as `Utah.Scripting.HmiVBScriptGlobalData` (`Subtype` `Global`, `IVBScriptAttributes.ReturnType`), the code in `ISimpleBaseScriptAttributes.IdCode` with every name replaced by `$k$i$`: `i` indexes the script's `CodeRelations` (an HMI tag, a system function), `k` is its kind. Reading it back means putting the names in again.
- **Custom web controls**: a `.zap21` archive carries the project's `UserFiles/CustomControls/{GUID}.zip` as a plain file (FR-016). TIA accepts a control from that folder through *My controls* › refresh in its toolbox, and refuses a package whose icon it cannot read ("Cannot parse image: ./assets/logo.ico"): the invented control needs a real `.ico`. Placing a control through Openness (`ScreenItems.Create<HmiCustomWebControlContainer>(name, type)`) is refused ("No control exists for given contained type value") even for a control TIA has accepted and with its exact `ContainedType`, so it is placed by hand (FR-017). Placed by hand, it is a Unified screen item (`ILScreenItemData`, `Subtype` `HmiCustomWebControlContainer`, the control's display name in the expando key `_ContainedControlDisplayName`, geometry under the usual short keys) with an `ExpandoReferences` to a `Utah.Globalization.HmiDynamicScreenItemResourceData`: `Name` the GUID, `FilePath` `CustomControls\{GUID}.zip`, and `Content`, a .NET binary serialization stream of the control's interface (`DynamicPropertyListStructure`, its properties such as `Level`), which the reader's NRBF reader can take. The package itself, HTML, CSS and JavaScript, is only the zip in the archive's `UserFiles`. Through Openness the placed control gives `ContainedType` (the GUID in braces) and its `Interface` (`PropertyName` and `Value`).

From the schemas, besides: `HmiScriptGlobalData`, `HmiScriptGlobalModuleData`, `HmiScriptGlobalSectionData`, `HmiScriptSimpleData` and the library types in `DL.Library.ScriptModuleLib`; and the Openness API listed below.

- **Unified scripts**: object types `Siemens.Simatic.Hmi.DL.Scripting.HmiScriptBaseData`, `HmiScriptEmbeddedData`, `HmiScriptSimpleData`, `HmiScriptGlobalData`, `HmiScriptGlobalModuleData`, `HmiScriptGlobalSectionData`, `HmiScriptModuleContainerData`. `IScriptAttributes` has `Code` (a `CodePartList` structure), `Language` and `Scope`; `IScriptModuleAttributes` has `Language` and `Scope`. A Unified screen item's `_#Dynamics` key (a map) probably names the scripts and dynamizations attached to it. Library script module types live in `DL.Library.ScriptModuleLib`.
- **Unified Openness**: `ScriptDynamization` (`ScriptCode`, `GlobalDefinitionAreaScriptCode`, `Async`) on properties, event handlers of screens and screen items, and `HmiSoftware.Scripts` (`HmiScriptModule`) for global modules. So the reference project can be built through the API, and the code read back as TIA's view.
- **Comfort/Advanced VB scripts**: `Siemens.Simatic.Hmi.Utah.Scripting` with `IVBScriptAttributes` (`ReturnType`) and `IBaseScriptAttributes` (`ParameterList`, `PreIdCode`); function lists (`HmiFunctionListData`) of system functions on events. Openness: `VBScriptFolder` with SimaticML import and export of each `VBScript`.
- **Custom web controls**: `Siemens.Engineering.HmiUnified.UI.Base.HmiCustomWebControlContainer` in Openness, and a schema type named `CustomWebControl`. Siemens' documentation has a control's package, a zip, copied into the project's `UserFiles\CustomControls` folder; not checked here. Whether an archive carries that folder, and what the database stores of it, is FR-016. Writing an invented control for the reference project needs a `manifest.json`, an `index.html` and its scripts, as Siemens documents the format.

## Modules

- `Storage`: decoding a `CodePartList`, a structure, as section 7.4 says; perhaps a general structure reader, shared with the `TypeSafeAddress` read of spec 0011.
- `Program/TiaHmiScripts`: the scripts, modules, VB scripts and custom web controls of each HMI device, by the HMI application each belongs to, as `TiaHmi` finds devices; `TiaHmiReport` gains them when asked for, so `tiadump hmi` without `--scripts` stays as fast as now.
- `Program/TiaHmiWriter`: the scripts in Markdown and JSON, and the dump's files: `scripts/<screen>/<item>.<event or property>.js`, `modules/<name>.js`, `vbscripts/<name>.vbs`, `custom web controls/<name>/...`.
- `src/TiaDump`: `--scripts` on `hmi`.
- `src/TiaViewer`:
  - Source view: the existing editor, with AvaloniaEdit's JavaScript and VB highlighting definitions.
  - `HmiScreenPreview`: a `Canvas` of the screen's size with a bordered box per screen item at its position, scaled to fit, the item's type and text in it; a click selects its row in `HmiView`.
  - `HmiWebControlPreview`: behind a button and a notice, unpacks the control into a temporary folder and shows its start page in `Avalonia.Controls.WebView` (ADR 0017), with `AdapterPreference` WebView2 only, WebView2's data folder in a temporary folder, top-level navigations and new windows away from the control's folder cancelled, and no handler for the page's messages. Blocking its other requests takes WebView2's own interface through `TryGetPlatformHandle()`; until that is built, the notice says the control may reach the network. The folders are removed when the preview closes. Without a web view, "Open in browser" with the same folder.
  - `src/TiaViewer` gains an `app.manifest` listing Windows 10 and 11 as supported, which the web view needs.

## Tests

- `TiaHmiScriptsTest`: one test per acceptance scenario of the first three stories, named after it, on the reference project, and on `TiaFileBuilder` files for what it cannot hold.
- The comparison with TIA's view of every script and control file, as `HmiReference` does for spec 0011 (FR-014).
- Viewer: the preview's layout (positions, scale) and the temporary folder's life tested on the library side or in `tests/TiaViewer.Tests` without a window; the web view itself checked by running the viewer on Windows and macOS, as the AOT spike does.
- A test that opening an archive with a custom web control and reading its report and source starts no web view and writes no temporary folder (FR-009, SC-003).

## ADRs

[ADR 0017](../docs/adr/0017-custom-web-controls-run-in-the-platforms-web-view.md), a draft until the AOT spike settles it. `docs/releasing.md` gains the package's notice.
