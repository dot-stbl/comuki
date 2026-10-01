## 1. Snapshot helper unit test

- [x] 1.1 Verify the `TranslatorEnvironment.Snapshot` filter (the `.Where(static pair => pair.Value is not null)` chain in `platform/src/host/Comuki.Host.Translator/Program.cs`) is in place — read `Program.cs` and confirm `be2af0a0`'s diff is on the branch; the snapshot omits unset entries.
- [x] 1.2 Add `TranslatorEnvironmentSnapshotShould` (or extend an existing snapshot test class) in `tests/unit/Comuki.Host.Translator.Unit.Runtime/`: three `[Fact]`s covering empty env (no `COMUKI_*` set) → returned dictionary is empty; one env var set → exactly that one entry is present with the right key + value; full env (all ten vars) → returned dictionary has ten entries, none of which is `null`. Verify by `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` and the new tests pass.
- [x] 1.3 Add `InternalsVisibleTo("Comuki.Host.Translator.Unit.Runtime")` if not already present (the snapshot helper is `file static class TranslatorEnvironment`; expose it via `[InternalsVisibleTo]` or a `public static` rewrapper) — verify by the new test compiling and finding the type.

## 2. ProfilesProvider regression test

- [x] 2.1 Add `WorkingDirectoryDefaultDoesNotThrowAsync` to `tests/unit/.../ProfilesProviderShould.cs`: construct options with `WorkingDirectory = Directory.GetCurrentDirectory()` (the default `TranslatorOptions` ships) and `ProfilesPath = null`, `ProfilesGitUrl = null`; call `PrepareAsync("v1")`; expect the documented warning log and no `ArgumentNullException`. Verify by `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` passing the new test alongside the existing four.
- [x] 2.2 Add a sibling fact `SnapshotWithUnsetWorkingDirectoryPreservesDefault` that exercises the full binder (`TranslatorOptions` bound from an `InMemoryConfigurationSource` carrying only the snapshot's "all env vars unset" payload) and asserts the bound `WorkingDirectory` equals `Directory.GetCurrentDirectory()`. Verify by the same unit run.

## 3. Boundary-doc note

- [x] 3.1 Add a one-line note to the `TranslatorEnvironment.Snapshot` doc comment (or to the class summary) reminding future contributors that every entry MUST either carry a `[Required]` annotation on `TranslatorOptions` or be safe at its declared default — i.e. you cannot add an option without `[Required]` AND without a default. Verify by `dotnet build comuki.slnx -c Debug` and the comment text grep.

## 4. Integration test workaround removal

- [x] 4.1 In `tests/integration/Comuki.EndToEnd.AgentLoop/AgentLoopHarness.cs`, remove the `["COMUKI_WORKING_DIRECTORY"] = "/work"` entry from the `Env` dictionary inside `ComputeStartRequest`. Confirm by the diff no longer containing that line.
- [x] 4.2 Replace the long-form "found production gap" comment above the `Env` literal with a 1-line reference to this OpenSpec change ("`COMUKI_WORKING_DIRECTORY` workaround retired — see OpenSpec change `fix-null-translator-working-directory`; the snapshot filter keeps the default"). Verify by `dotnet build comuki.slnx -c Debug` (the integration test project still compiles; the comment is plain text).

## 5. Cross-slice gate

- [x] 5.1 Update the relevant cross-link in `openspec/changes/harden-pi-worker-sandbox/tasks.md` §7 (follow-ups) if it currently does not mention this change as a contract predecessor — add one bullet pointing at this change's spec scenario "An unset required option still fails the boot" as the invariant harden-pi-worker-sandbox must preserve when it later stamps keys per-claim. Verify by reading the file post-edit and the bullet present.
- [x] 5.2 Run `dotnet build comuki.slnx -c Debug` (warnings-as-errors, analyzers, format) — exit 0. Run `dotnet run --project tests/unit/Comuki.Host.Translator.Unit.Runtime` — all tests pass, including the new `TranslatorEnvironmentSnapshotShould` and `ProfilesProviderShould.WorkingDirectoryDefaultDoesNotThrowAsync`. Verify by the build output and the test summary.

## 6. Archive gate

- [x] 6.1 `openspec validate fix-null-translator-working-directory --strict` reports no findings and `openspec status --change fix-null-translator-working-directory` reports all four artifacts `done`. Verify by exit 0 on both commands.