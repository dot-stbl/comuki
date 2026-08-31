---
name: project
description: Show or switch the active project context and its settings.
---

When the user invokes /project:

1. Show the active project: repository, compute, model endpoints, feature
   flags (knowledge, verify, approvals).
2. If the user names another project, switch the session context to it.
3. Setting changes (quotas, approvals on/off, debug) apply live; secrets and
   provider URLs always go through env, never chat.
4. For structural content (profiles, admission, playbooks) point to the
   project's git - chat does not edit those.
