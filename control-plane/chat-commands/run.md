---
name: run
description: Start a new run from a ticket, a prompt, or a plan, and follow its progress.
---

When the user invokes /run:

1. Collect the target: a linked ticket, free-text task, or an existing plan.
2. Confirm the plan mode (single worker / brief-first / full graph) and
   whether approval is required (default: yes).
3. Create the run, then report its id and where to watch progress.
4. On follow-up questions, report the current run status from the journal.
