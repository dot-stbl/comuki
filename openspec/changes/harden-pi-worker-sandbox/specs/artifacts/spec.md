## ADDED Requirements

### Requirement: Translator may drain the bundle
The Translator MAY upload `brief.json` / `result.json` / `pins.json` under the run prefix before the REST complete or fail call. Upload uses the same `IRunArtifactStore` contract as the packager. A drain error SHALL be journaled and SHALL NOT block complete or fail.

#### Scenario: Drain wins the race
- **WHEN** the Translator uploads the three objects and then completes
- **THEN** the packager finds the run already bundled and does not overwrite the objects

#### Scenario: Drain failure still completes
- **WHEN** the artifact store rejects the upload
- **THEN** the work item still completes or fails and the packager remains the fallback
