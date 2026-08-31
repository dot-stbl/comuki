---
name: help
description: List available commands and what Comuki can do in this session.
---

When the user invokes /help:

1. List the built-in commands (init, run, status, stop, plan, project) with
   one line each.
2. Mention custom commands if the project's git defines any.
3. Offer next steps based on state: no project -> /init; open runs ->
   /status; waiting approval -> /plan.
