# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Layout

This repo uses a single-context domain docs layout.

The expected locations are:

- `CONTEXT.md` at the repo root for project domain language and glossary.
- `Docs/adr/` for architectural decision records.

If these files do not exist yet, proceed silently. Do not flag their absence or create them just because they are missing. The domain-modeling workflows can create them later when useful terms or decisions are actually resolved.

## Before exploring, read these

- Read `CONTEXT.md` if it exists.
- Read relevant ADRs under `Docs/adr/` if that directory exists.
- Also read `AGENTS.MD` for repo-specific collaboration rules.

## Use the glossary's vocabulary

When output names a domain concept, such as a map node, wave, hive boss, deployment phase, shop pool, or logistics level, use the term as defined in `CONTEXT.md` if it exists.

If the concept is not in the glossary yet, either use the existing wording from the codebase and `AGENTS.MD`, or note the gap for a future domain-modeling pass.

## Flag ADR conflicts

If output contradicts an existing ADR, surface it explicitly rather than silently overriding the decision.
