## Why

Every piece of user-facing copy in the dashboard is a literal English string
baked into components — 159 non-test `.tsx` files plus ~50 label-bearing
`.ts` modules (nav sections, permission denials, relative time, error
fallbacks, toast messages). The product is localised by owner decision —
English stays the default, Russian is an additional locale, and the popular
open-source locale set ships alongside it right away («дефолт по сути
английский, русский же доп язык, можем еще языки сразу добавить популярные
в opensource»). The repo's global FE rule
(`~/.agents/rules/typescript/workspace-and-i18n.md` §4) already prescribes
i18next with parallel-maintained locales; the dashboard has none of
it. Migrating now, while the surface is ~200 files of mechanical copy moves
and no formatting infrastructure to untangle, is cheaper than after more
screens land.

## What Changes

- Adopt **i18next + react-i18next** (pinned in `dashboard/package.json`;
  no http backend — locale JSONs are native Vite `import.meta.glob`
  imports; `en` bundles eagerly as default and fallback, every other
  locale loads lazily as a chunk on switch).
- **All user-facing copy goes through `t()`**: JSX text, label/title/
  description/placeholder/aria-label props, empty-state strings, toast
  messages, and label maps in non-component modules (via the shared i18n
  instance, not hooks).
- **Locale set**: `en` (default, source of truth) + `ru` + the OSS-popular
  six — `de`, `es`, `fr`, `pt-BR`, `zh-CN`, `ja` — eight locales total,
  enumerated by one registry constant. Locale files: per-namespace JSONs at
  `dashboard/src/shared/i18n/locales/{lng}/<namespace>.json`, one
  namespace per domain plus `shell` and `kit` — key sets identical across
  the full set; `en` is hand-authored, `ru` is owner-reviewed per wave,
  the other six ship as machine-generated drafts per wave.
- **Default language `en`**, persisted choice (`localStorage`, mirroring
  the theme key pattern), a registry-driven locale switcher in the shell
  next to the theme control enumerating all shipped locales by native
  endonym, and `document.documentElement.lang` kept in sync.
- **Explicit copy boundary**: server-originated strings (ProblemDetails
  detail), LLM/worker-authored prose (run step names, chat messages,
  artifact content) and mock seed *data* pass through untranslated;
  dev logs and test names are exempt per the global rule.
- **Tests**: the vitest setup initialises the real i18n instance with `en`
  active, so existing literal assertions keep passing while now exercising
  the locale files; a dedicated parity test asserts key-set equality
  across the full locale set; the FE rule audit (`scripts/rule-audit.ts`)
  gains a literal-JSX-copy ban.
- Migration lands in **per-domain waves** (projects first — it is hot and
  carries the `add-project-visuals` follow-on controls — then shell/auth/
  home, duty screens, management flows, long-tail domains, shared kit
  last), each wave gated by the full FE gate.

## Capabilities

### New Capabilities

- `dashboard-i18n`: multi-locale rendering (en default; ru, de, es, fr,
  pt-BR, zh-CN, ja shipped) of all dashboard user-facing copy — locale
  parity across the set, fallback and missing-key behaviour,
  pluralisation/interpolation, locale persistence and switching, and the
  copy boundary between localisable chrome and pass-through data.

### Modified Capabilities

(none — no existing spec pins dashboard copy language)

## Impact

- Frontend only: `dashboard/` across `src/app/` (providers, shell, nav,
  command palette, theme control area), all 20 `src/domains/*`,
  `src/routes/*` heads, `src/shared/ui/` kit labels and
  `src/shared/{lib,session,api}` copy-bearing helpers.
- New deps: `i18next`, `react-i18next` (bun add, pinned; no backend
  loader, no language detector).
- Tests: ~52 test files asserting on rendered copy keep working via the
  real-i18n test setup; one new parity test; `scripts/rule-audit.ts`
  extended (runs on `predev`/`prebuild`).
- No backend, wire, or database changes; no new endpoints; `formatCost`
  and date/number formatting stay as-is (explicit non-goal).

## Non-goals

- Backend localisation — ProblemDetails and API errors stay English; the
  FE renders its own localised fallbacks around them.
- Locale-aware number/currency/date formatting (`formatCost` keeps its
  `$` shape; `relative-time` localises its unit words, not its figures).
- Locales beyond the shipped eight (adding one is a registry entry plus
  its catalogues — a recorded extension path, not a re-plan); RTL layouts
  (no shipped locale needs it); per-user server-stored locale preference
  (client-persisted only, like theme).
- Mock seed data translation — seeds are fixtures standing in for server
  data; only the chrome around them localises.
- Per-namespace lazy loading *within* a locale — non-default locales
  already ship as lazy whole-locale chunks; finer splitting is a recorded
  follow-up trigger, not a default.
- Translation quality review of the machine-drafted six — parity and
  fallback keep them structurally sound; quality judgement is owner and
  community scope, recorded as policy in the design, not an agent task.
