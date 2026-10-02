---
key: repair-boundary
title: Repair boundary
description: A bounded re-execution wrapper: each generation re-runs the body on a typed failure outcome, never as a runtime back-edge.
owner: WorkItem
parameters: repair-boundary-config-v1
ports:
  - closed
  - exhausted
  - escalated
evidence:
  - generation-record
  - failing-evidence
risk: medium
idempotency: required
approval_floor: 0
---

A generator of numbered generations referencing the failing evidence.
Only the `closed` port advances the procedure; `exhausted` and the
declared `escalated` ports route the run to a human decision. The
materialized execution graph stays acyclic — a generation is a new
materialization, never a runtime back-edge.
