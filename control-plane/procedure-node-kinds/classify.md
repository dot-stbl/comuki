---
key: classify
title: Classify
description: A brain operation that reads the intake envelope and emits a typed classification the routing rules consume.
owner: BrainOperation
parameters: classify-prompt-v1
ports:
  - classified
  - ambiguous
  - unsplittable
evidence:
  - classification
  - confidence
risk: low
idempotency: inherent
approval_floor: 0
---

A brain-side prompt that classifies the intake envelope. The result is
typed — never free-form — and consumed by routing rules that pick the
remainder of the graph.
