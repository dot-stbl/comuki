## ADDED Requirements

### Requirement: Verify-only Translator loop
When the claimed work item is a verify execution, the Translator SHALL run clone (if needed), restore opcodes, then class-advertised verify opcodes, and SHALL NOT be required to spawn pi. The REST claim/heartbeat/complete/fail contract is unchanged. Verify opcode failure SHALL `fail` the item with the opcode name and exit code.

#### Scenario: Verify completes without pi
- **WHEN** a verify item of class `net10-sdk-bun` restores and `dotnet build comuki.slnx -c Debug` exits 0
- **THEN** the item completes successfully and no pi process was started

#### Scenario: Verify fail does not spawn pi
- **WHEN** `dotnet build` exits non-zero in the verify loop
- **THEN** the item is failed over REST and pi is not started
