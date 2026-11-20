# Update log

## 2026-11-20

* **Update**: [spec 0005](../specs/0005-viewer-opc-ua.md) and its plan: the viewer shows each PLC's OPC UA report as the first entry under the PLC, made when first shown and kept out of search and export.
* **Update**: [the format](FORMAT.md) gains section 18, where a PLC's OPC UA settings, server storage and roles are stored and how tags and members carry external access, checked on three more samples (C, D, E); [spec 0004](../specs/0004-opc-ua-report.md) and its plan describe `tiadump opcua`, which reports them.

## 2026-11-18

* **Update**: [releasing](releasing.md): the release workflow builds the Mac zips too, on a Mac runner with `tools/release_macos.sh`, and attaches them with the Windows ones in one last job.
* **Update**: [releasing](releasing.md) adds the Mac: `tools/release_macos.sh` builds the viewer and `tiadump` for Apple silicon and Intel in one, and `tools/package_release.py` packs them with the same notices as the Windows zips, which the 0.1.0 Mac zips first lacked.
* **Creation**: [releasing](releasing.md) says how a version is released: a tag starts a workflow that builds the Windows programs through Native AOT, and `tools/package_release.py` packs them with the notices of the packages they contain.
* **Update**: [ADR 0010](adr/0010-readme-pictures-are-rendered-headless.md) records that the README's pictures are rendered headless by `tools/ReadmePictures`, after the first script that took them was lost; [comparing with TIA](comparing-with-tia.md) says how to run it and the [architecture](architecture.md) lists it.
* **Update**: the [architecture](architecture.md) lists the viewer's new test project, which came with [spec 0003](../specs/0003-viewer-menus.md) for the viewer's menus, About window, recent files and the file types its open dialog accepts.
* **Update**: [ADR 0009](adr/0009-apache-2-0-with-the-commons-clause.md) records the licence, Apache 2.0 with the Commons Clause and additional terms, and why it fits a tool used at work that nobody should sell, in whole or in part, without permission.
* **Creation**: the bundle, in the layout of the agentic repository template: its [index](index.md), [guide](guide.md), [workflow](workflow.md), the eight [decisions](adr/index.md) inherited from the template, and an [architecture page](architecture.md) for TiaTools. [The format](FORMAT.md) joins it with frontmatter, and the README's section on comparing with TIA moves to [its own page](comparing-with-tia.md).
