---
key: agent
title: Agent
description: A work item under claim/lease — a worker profile picks it up and produces typed output per the parameter schema.
owner: WorkItem
parameters: agent-parameter-schema-v1
ports:
  - succeeded
  - failed
evidence:
  - worker-report
  - output-artifact
risk: medium
idempotency: required
approval_floor: 0
---

A work item leased to a worker profile. The claim/finalize/leak machinery
of the W1 execution spine owns lease state; the procedure coordinator
tracks position-in-procedure and reads the instance state through the
typed projection.
