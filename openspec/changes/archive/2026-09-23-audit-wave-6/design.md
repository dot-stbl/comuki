## Design (audit only — no code)

This change documents what landed between the previous backfill
(`backfill-wave6-platform`, issues #20–#26 + the openapi tweak) and the
current master tip (post Wave 6 + FE wire-up). The code is already in
place; the audit writes the corresponding spec surface and tasks. No
runtime change in this change.

The shape follows the existing OpenSpec workflow: a `proposal.md` (this
file), a `tasks.md` (the actionable remediation list), and `specs/`
delta specs for the capabilities whose main spec is missing requirements.
The brand-new `artifacts` capability is added directly under
`openspec/specs/artifacts/spec.md` because no main spec exists.

The proposal does NOT cover the Wave 6 issue set already in
`backfill-wave6-platform`; that change is the prior baseline. The
audit covers everything that landed AFTER that change landed.
