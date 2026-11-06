# Instructions for agents

Read README.md, docs/CURRENT-STACK.md and docs/VALIDATION.md first. Operator language is Russian. This is a real Windows trading installation; do not invent successful installation or fill confirmation.

Current standing from the authenticated chat on2026-11-06: trading tests use screen controls on Sim101; no trading API calls. Keeping historical Sim101ApiBridge source is not permission to call it. No live/OneUp executions or adoption of unrelated orders. Check exact account/instrument/quantity before each action; after unknown UI outcome reread position/orders before retrying. Authentication and trust prompts belong to the human.

Fixes and test evidence go in PRs. Preserve terminal-intent/authorization/ownership boundaries. No market tape, credentials, account IDs, logs, sqlite, personal workspace XML or third-party DLLs in public commits/releases. Build output with vendor code stays .local. A source archive is not a standalone NinjaTrader executable. Do not silently overwrite unrelated installed customizations.

Panel risk budget300 includes assumed commission2 per MNQ round trip, applies to remaining open quantity, never loosens an already tighter stop. Preserve closed-M5 rules and descriptive historical semantics in the separate DR Lab project. New account, broker or instrument requires explicit contract mapping and its own paper validation; IB support is not implemented.

Run tools/test.ps1 after lifecycle/risk changes, build.ps1 for native integration. Mark native/UI cases NOT_TESTED until observed. Capture failures before repairing. When changing installed DLL/source, back up and follow normal compile/reload or closed-process installer; never replace loaded DLL by force. Do not trade to manufacture evidence after an inconclusive UI observation.
