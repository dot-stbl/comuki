## Why

Intake (S6) shipped: webhooks, sources, admission rules, inbox claim, native
tickets, sync-back. No OpenSpec capability documents the surface.

## What Changes

- Add capability `intake` covering anonymous webhooks, source CRUD, admission
  rules, inbox/claim, native ticket create, and sync job persistence.

## Capabilities

### New Capabilities
- `intake`: tracker ingress, admission, inbox, sync-back

### Modified Capabilities
- (none)

## Impact

Docs only. Code in `platform/src/modules/Intake` and `Comuki.Host/Intake`.

## Non-goals

- New tracker providers beyond github / gitlab / jira / yandex-tracker
- Changing webhook auth model (signature-as-auth)
