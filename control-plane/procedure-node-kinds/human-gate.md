---
key: human-gate
title: Human gate
description: Blocks the run until the procedure's approval policy (0, 1, or 2 distinct humans) records a decision.
owner: Decision
parameters: human-gate-config-v1
ports:
  - gate.approved
  - gate.rejected
  - gate.expired
evidence:
  - decision-record
risk: high
idempotency: inherent
approval_floor: 1
---

A node whose only job is to stop the run and surface a decision to the
operator attention layer. The number of distinct approvers is bounded by
the procedure's approval policy; one person approving twice never
satisfies a two-approval gate.
