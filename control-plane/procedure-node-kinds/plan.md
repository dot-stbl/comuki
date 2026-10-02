---
key: plan
title: Plan
description: A brain operation that turns a classified envelope into an ordered list of typed work items to execute.
owner: BrainOperation
parameters: plan-prompt-v1
ports:
  - planned
  - empty
evidence:
  - work-item-list
  - plan-rationale
risk: medium
idempotency: required
approval_floor: 0
---

A brain-side prompt that decomposes the work into typed work items. The
fan-out boundary expects a list of work items; an empty list routes to
the `empty` port and the procedure ends early without further work.
