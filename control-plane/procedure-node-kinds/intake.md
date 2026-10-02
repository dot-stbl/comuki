---
key: intake
title: Intake
description: Accepts an inbound ticket and emits the canonical intake envelope that downstream nodes operate on.
owner: WorkItem
parameters: intake-envelope-v1
ports:
  - accepted
  - deferred
  - rejected
evidence:
  - intake-record
  - source-ref
risk: low
idempotency: inherent
approval_floor: 0
---

First node a procedure runs. Receives the raw inbound ticket
(source-system id, requester identity, payload) and produces the intake
envelope the rest of the graph consumes. Has no upstream dependency.
