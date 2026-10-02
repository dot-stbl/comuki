---
key: escalate
title: Escalate
description: A terminal node that carries the run to a human decision — the operator attention surface owns the next step.
owner: Decision
parameters: escalate-payload-v1
ports:
  - escalated
evidence:
  - escalation-record
  - trigger-evidence
risk: high
idempotency: inherent
approval_floor: 0
---

The boundary owner chose retry, replace, waive, or fail — escalation
hands the choice to the operator. After escalation, the run is parked
on the human-decision node, never on a model.
