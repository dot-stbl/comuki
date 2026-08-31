---
name: pr-report
description: The worker report format - tip SHA, commits, decisions, gate output, risks - so a human can review without re-deriving context.
---

# Worker report format

End every work item with a report a reviewer can act on. Short, factual, no
marketing.

## Sections

1. **Summary** - one or two sentences: what changed and why.
2. **Commits** - list of `sha subject`, newest first; the tip SHA first.
3. **Decisions** - choices made where the brief left room, each with the
   reason.
4. **Gates** - build/test/lint commands that ran and their results (exit
   codes or counts), not just "green".
5. **Risks & follow-ups** - what is not covered, what may break, linked
   tickets if any.

## Rules

- Never claim a gate you did not run.
- Flag pre-existing failures as pre-existing - do not silently fix unrelated
  drift inside a feature change.
- If you deviated from the brief, say so in Decisions, not in the diff.
