---
type: Architecture Decision Record
title: "TiaTools is licensed under Apache 2.0 with the Commons Clause"
description: "TiaTools is licensed under Apache 2.0 with the Commons Clause and additional terms: free to use, change, share and use in paid work, but not to sell, in whole or in part, without permission."
generated: { by: claude-code/claude-opus-5-5, at: 2026-11-18T12:00:00Z }
status: draft
---
# TiaTools is licensed under Apache 2.0 with the Commons Clause

Nearly everyone who opens a TIA Portal project does so at work, so a licence that forbids commercial use would turn away the people the tool is for. What the author wants to keep is the right to decide who sells it: nobody should take TiaTools, or part of its code, into a product and sell that without asking, while using it, changing it and using it in paid work such as consulting stays free. Apache 2.0 grants use, change and redistribution with a patent grant; the Commons Clause takes away the right to Sell; additional terms in `LICENSE` widen Sell to any software that contains part of the code, and to a paid hosted service, and narrow it so that paid work done with the tool is not selling it.

## Considered options

- Apache 2.0 alone: anyone could sell TiaTools, or a rebranded copy, without asking.
- The Commons Clause unchanged: it counts paid consulting about the software as selling, and lets a product sell part of the code when that part is not a substantial share of its value.
- Functional Source License: allows professional services and blocks competing products, but every version becomes Apache 2.0 or MIT two years after release, after which it can be sold.
- PolyForm Noncommercial, or the Business Source License: forbid or limit the commercial use the tool exists for.
- PolyForm Perimeter or Shield: forbid any competing product, free forks included, and not a product that merely contains part of the code.
- GPL or AGPL: allow selling, and internal use is not distribution, so nothing is kept.

## Consequences

TiaTools is source-available, not open source in the OSI sense; some companies' policies treat that differently, and GitHub does not recognise the licence. The additional terms are the author's own wording, not a published licence, and have not been reviewed by a lawyer. Contributions are taken under the same licence; a commercial licence that includes others' code needs their consent, or a contributor agreement. A later version can move to a more permissive licence; a version once released under it stays under it. The skills from mattpocock/skills keep their MIT licence (THIRD-PARTY-NOTICES.md).
