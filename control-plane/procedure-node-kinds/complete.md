---
key: complete
title: Complete
description: The terminal success node — the procedure closes here when the body reaches a green outcome.
owner: WorkItem
parameters: complete-envelope-v1
ports:
  - closed
evidence:
  - completion-record
risk: low
idempotency: inherent
approval_floor: 0
---

The procedure has reached an outcome the operator accepts. Side-effects
declared by the body (a pushed branch, a published artifact) are durable
before this node fires.
