---
key: review
title: Review
description: A brain operation that reads an artifact and emits an approval-grade review the operator surfaces in Studio.
owner: BrainOperation
parameters: review-prompt-v1
ports:
  - approved
  - changes-requested
  - inconclusive
evidence:
  - review-report
risk: medium
idempotency: inherent
approval_floor: 0
---

A brain-side review that produces structured feedback. The studio renders
the report alongside the artifact; a `changes-requested` outcome routes
back into the body that produced the artifact.
