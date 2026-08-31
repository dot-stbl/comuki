---
name: status
description: Show the current state of runs, work items, and workers for the active project.
---

When the user invokes /status:

1. Read the active runs of the current project (or the one the user names).
2. Show per run: status, elapsed time, work items by state, worker count.
3. Highlight anything needing attention: failed items, waiting approvals,
   stalled heartbeats.
4. Keep it compact: a table plus exceptions, not a full event dump.
