## Design (docs backfill)

No new runtime design — behavior already shipped. Specs are reverse-engineered
from `Modules.Chat`, `Modules.Memory`, Host chat controllers, and Brain
`memory.search`.

### Decisions captured
- Subject ownership → 404 for foreign sessions
- Checkpoints + messages as durable short-term memory
- Facts supersede by topic_key; ephemeral 14d sweep
- EmptyMemoryDigest remains a valid composition fallback until digest is wired
- Learning candidates table exists; approve→git deferred

### Non-goals
- Implementing missing digest wiring or learning approve pipeline in this change
