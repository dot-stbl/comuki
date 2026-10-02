## ADDED Requirements

### Requirement: Admission before prepare
The Translator loop SHALL obtain a successful SlotAdmission (or fail the item with the admission code) before clone, restore, or pi spawn. Existing prepare steps (clone, restore, profiles overlay) SHALL run only after admission succeeds.

#### Scenario: Denied admission skips clone
- **WHEN** admission returns `admission.env_unconfirmed`
- **THEN** the Translator does not clone and does not start pi
