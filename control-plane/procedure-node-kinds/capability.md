---
key: capability
title: Capability
description: Routes an operation through the capability broker — the broker enforces the exposure class; the engine never calls the target directly.
owner: BrokerOperation
parameters: capability-call-v1
ports:
  - succeeded
  - refused
  - failed
evidence:
  - broker-result
  - capability-id
risk: medium
idempotency: required
approval_floor: 0
---

The broker's exposure class decides what the operation may do; the
procedure specifies what it wants done. `refused` means the broker
denied under policy — distinct from `failed`, which means the capability
ran and reported an error.
