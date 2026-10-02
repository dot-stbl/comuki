---
key: join
title: Join
description: Waits for all upstream lanes to reach a terminal port and aggregates their outcomes into a single result.
owner: WorkItem
parameters: join-config-v1
ports:
  - all-passed
  - has-failed
evidence:
  - lane-results
risk: low
idempotency: inherent
approval_floor: 0
---

Synchronizes parallel lanes. The runtime decides when the boundary is
satisfied — the join node carries no model decision. Inconclusive ports
do not satisfy the join and propagate to the boundary owner instead.
