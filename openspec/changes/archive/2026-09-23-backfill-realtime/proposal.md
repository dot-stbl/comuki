## Why

SignalR runs hub (S7) and journal broadcast with attention groups shipped.
Host/runs specs still stop at REST composition and domain lifecycle.

## What Changes

- Add capability `realtime` for hub joins, group naming, broadcast, attention.
- Extend `host` to acknowledge SignalR composition + hub route.

## Capabilities

### New Capabilities
- `realtime`: RunsHub, journal broadcast, attention signals

### Modified Capabilities
- `host`: maps `/hubs/runs` and registers SignalR broadcaster

## Impact

Docs only. Code in `Comuki.Host/Realtime`.

## Non-goals

- Chat token streaming over SignalR
- Presence / typing indicators
