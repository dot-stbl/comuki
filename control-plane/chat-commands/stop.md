---
name: stop
description: Stop a run or a single worker - soft stop first, force only on request.
---

When the user invokes /stop:

1. Identify the target: run id, worker id, or "the current run" when only one
   is active.
2. Issue a soft stop (Stop command over gRPC); report the acknowledgement.
3. If the user asks to force, kill the container and explain the difference:
   a soft stop lets the worker flush its report, a force stop does not.
4. Confirm the resulting run and work-item status after stopping.
