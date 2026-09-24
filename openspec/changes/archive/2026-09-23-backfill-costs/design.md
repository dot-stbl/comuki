## Design (docs backfill)

Costs module records usage_events; project settings hold soft/hard USD micros;
host gate cancels + journals `budget.exceeded`.

### Note
Minimal-API costs route still documents a TODO for `cost:read` attribute —
spec states intended permission and notes pending wiring.
