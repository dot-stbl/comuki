---
name: plan
description: Show, build, or refine the plan for a run - the graph of profile launches and dependencies.
---

When the user invokes /plan:

1. With an active run, show its plan: work items as profile launches with
   dependencies, statuses, and assigned workers.
2. Without one, enter planning: clarify the goal, propose a decomposition
   (which profile per work item), and ask for approval before applying.
3. On request, replan a failed or changed run: mark what stays and what is
   replaced.
4. Never apply a plan without explicit approval unless the project disabled
   approvals.
