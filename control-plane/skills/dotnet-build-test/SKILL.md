---
name: dotnet-build-test
description: How to verify a .NET change in a Comuki-style solution - build gate with warnings-as-errors, touched test suites, format drift fix.
---

# Build & test (Comuki .NET projects)

The build is the gate - never report done on a red or drifting build.

1. Build the whole solution, not a single project:

   ```
   dotnet build <solution>.slnx -c Debug
   ```

   Warnings are errors, and the same command runs the format check
   (`VerifyFormatOnBuild`). Exit code 0 or it is not done.

2. Run the touched test suites. xUnit v3 uses the Microsoft Testing
   Platform - `dotnet test` does not discover the tests:

   ```
   dotnet run --project tests/unit/<Project>.<Kind>
   ```

3. Format drift -> fix, review the diff, then rebuild:

   ```
   dotnet format <solution>.slnx --severity hidden
   ```

   Never commit a blind reformat of files you did not touch.

4. Report which gates ran, their exit codes, and anything you could not
   verify.
