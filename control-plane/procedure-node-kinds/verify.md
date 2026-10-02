---
key: verify
title: Verify
description: Runs a typed verifier against an artifact and declares one of four ports: passed, failed, inconclusive, infrastructure-error.
owner: BrokerOperation
parameters: verify-call-v1
ports:
  - passed
  - failed
  - inconclusive
  - infrastructure-error
evidence:
  - verifier-report
  - artifact-hash
risk: low
idempotency: inherent
approval_floor: 0
---

The verifier exists in the pinned verifier catalog; an unknown verifier
is a compile-time refusal. Infrastructure-error is a distinct port from
inconclusive — it means the verifier never ran, not that it ran and was
uncertain. Only `failed` opens a semantic repair generation.
