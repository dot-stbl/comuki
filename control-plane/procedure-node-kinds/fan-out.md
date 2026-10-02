---
key: fan-out
title: Fan-out
description: Forks the graph into parallel lanes, one per input work item declared by the upstream plan node.
owner: WorkItem
parameters: fan-out-config-v1
ports:
  - dispatched
evidence:
  - lane-list
risk: low
idempotency: inherent
approval_floor: 0
---

A structural node the runtime computes at materialization time. The number
of lanes is bounded by the procedure's declared fan-out range; an overflow
is a compile-time refusal (procedure policy), not a runtime surprise.
