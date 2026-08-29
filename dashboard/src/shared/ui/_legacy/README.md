# `_legacy` — shadcn / radix quarantine (ADR-0001 strangler)

Do **not** add new components here. Domains migrate off these wrappers onto
the kit in `shared/ui/` (CSS Modules + React Aria). Temporary path shims
at `shared/ui/<name>.tsx` re-export from here until each primitive is ported.
