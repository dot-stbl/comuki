## Context

See proposal.md for why. Constraints that shape the how:

- Zero i18n today: verified by grep — no `i18next`, no `useTranslation`,
  no locale files in `dashboard/`. All copy is literal English inline in
  components and label maps. The FE rule
  `~/.agents/rules/typescript/workspace-and-i18n.md` §4 is the source
  this change encodes: `useTranslation` + `t('key')`, parallel-maintained
  locales, dev logs and test names exempt. The owner decision of
  2026-09-25 extends the set beyond en+ru: English stays default, and
  the popular OSS locale set ships from day one — the set, its registry
  and its authorship policy are D13/D14.
- Surface measured honestly (non-test, non-story, non-generated):
  - **159 `.tsx` files** carry copy (JSX text nodes or label-bearing
    props: `label`/`title`/`description`/`placeholder`/`summary`/
    `emptyLabel`/`aria-label`/tooltip `content`) across `app/`
    (~12), `domains/` 20 domains (~115), `routes/` (~25 heads),
    `shared/ui/` (~12 kit components).
  - **~50 `.ts` modules** carry user-facing words without JSX:
    `app/layout/nav.ts` + `nav-sections.ts`, `app/search/*` (palette
    sections), `shared/session/permissions.ts` (`needsLabel` — "not
    available", "needs approver or operator"), `shared/lib/relative-time.ts`
    ("just now", "8 min", "in 12 d"), `shared/api/problem.ts` fallbacks,
    domain label/status maps (`attention.ts` verdicts, `queue.ts`,
    `providers.ts`, `tabs.ts`, `gate.ts`, `login-failure.ts`…), and 23
    literal `toast(...)` calls.
  - **52 test files** assert on rendered English copy via text/role-name
    queries (177 test files total).
- Entry chain: `index.html` (`lang="en"`, theme pre-paint script) →
  `src/app/main.tsx` → `AppProviders` (`src/app/providers.tsx`:
  ThemeProvider → QueryClientProvider → AuthBoot → RealtimeProvider +
  Toaster). The theme already demonstrates the persistence pattern this
  change reuses (`localStorage` key `comuki-ui-theme`).
- Tests: vitest + jsdom, one global `vitest.setup.ts`, `globals: true`;
  no per-test i18n wrapper exists.
- `add-project-visuals` (open change) adds four controls whose copy
  lives in `project-identity-editor.tsx` / `tag-entry-field.tsx` — they
  ride this change's projects wave (already in the file list above).
- DESIGN.md and AGENTS.md take no language stance for the dashboard;
  `openspec/config.yaml` says chat with the owner is Russian, public
  docs English. EN default is therefore an open-and-shut choice (D3).
- The backend speaks English-only ProblemDetails; `requestFailureMessage`
  renders server `detail` when present and FE fallback copy when not.

## Goals / Non-Goals

**Goals:**

- One translation spine for the whole dashboard — components, kit
  labels, and non-component word maps — with key-set parity across the
  full shipped locale set enforced by the ordinary gates
  (`bun run test`, `audit:fe`).
- A migration that can land wave-by-wave with the gate green after each
  wave, not a big-bang rewrite.
- Tests that survive the migration mostly unchanged (D6) and afterwards
  fail on copy regressions by design.

**Non-Goals:** (design-level, beyond proposal's)

- `Intl`-based number/currency/date formatting — figures keep their
  current shapes; only the words around them localise.
- Server-stored locale on the session/user — client-persisted only.
- Translation of the Storybook-only decorator copy beyond what stories
  inherit from real components.

## Decisions

### D1. Central locale registry, one namespace per domain

Locale files live at `dashboard/src/shared/i18n/locales/{lng}/
<namespace>.json` for every locale of the shipped set (D13). Namespaces: one per domain (`projects`, `runs`,
`tasks`, `queue`, `chat`, `identity`, `sources`, `approvals`, `cost`,
`compute`, `home`, `knowledge`, `models`, `observability`, `verify`,
`settings`, `inbox`, `artifacts`, `auth`) plus `shell` (layout, nav,
command palette, theme area), `kit` (shared/ui labels), and `common`
(relative time, permission denials, error fallbacks, shared empty
states). Components call `useTranslation("projects")` and keys are
addressed namespace-relative.

**Alternative:** per-domain colocated `domains/<x>/locales/` — rejected:
parity tooling, chunk bundling and the "find every string" grep all want
one registry; colocated files scatter the per-locale mirror across twenty
trees and invite per-domain drift in file shape.

**Alternative:** one giant `translation.json` — rejected: merge conflicts
on a twenty-domain product and no ownership boundaries.

### D2. Key naming: readable dot-path slugs

Keys are lowercase dot-paths scoped to the namespace and named after the
concept, not the wording: `registry.empty.filtered`, `form.name.label`,
`detail.facts.slug`, `denial.needsApprover`. Screen → element → state.
Pluralisation uses i18next suffixes (`items.one` / `items.few` /
`items.many` / `items.other` per locale grammar). Interpolation is
`{{count}}`, `{{name}}` — always escaped.

**Alternative:** opaque ids (`registry.empty.t1`) — rejected: grep-hostile
and review-hostile; English-readable keys make the ru file auditable by
side-by-side diff. The cost (renaming a key when the concept renames) is
the same cost the component itself pays.

### D3. Default language `en`, persisted switch, no sniffing

Default `en`; the choice persists under `localStorage` key
`comuki-locale` (sibling of `comuki-ui-theme`), set by the
registry-driven locale control (D12/D13) placed with `theme-control` in
the topbar; `document
.documentElement.lang` is kept in sync (including on switch, which
re-renders without reload — react-i18next re-renders on language change
by design). **Alternative:** first-visit `navigator.language` sniffing —
rejected: the board is a dense ops surface where a surprise language is
more disruptive than a one-click switch, and a deterministic default is
testable. **Alternative:** RU default (owner speaks Russian) — rejected:
the owner decision names English the de-facto default («дефолт по сути
английский»); the spec keeps the default a single constant to flip
later.

### D4. `en` bundled eagerly; every other locale lazy per-locale chunks

Revisited for the eight-locale set — the original two-locale eager
decision is superseded (8 × "tens of KB" breaches the previously
recorded ~150 KB threshold by construction, and grows with every wave).

- **`en` imports eagerly** as native Vite JSON modules into one i18n
  instance initialised in `src/shared/i18n/index.ts` (resources inline).
  `en` must be synchronous: it is the boot default (D3), the fallback
  chain's terminus (D8) and the test language (D6). An async default
  would flash key paths on first paint — the exact failure that got the
  http-backend alternative rejected originally.
- **Every non-default locale loads lazily.** `import.meta.glob
  ('./locales/*/*.json')` yields Vite-hashed per-file chunks; a
  `loadLocale(lng)` helper awaits the locale's namespace imports and
  `addResourceBundle`s them before `changeLanguage` resolves. The
  switcher shows a brief pending state while the chunk loads (small,
  same-origin, fingerprinted). An operator who never switches downloads
  exactly one locale's payload.

**Alternatives rejected:** eager × 8 (breaches the size threshold by
construction; ~7/8 of the bytes are dead weight for the median
operator); `i18next-http-backend` (fetches static assets the bundler
already fingerprints — worse caching and error handling than hashed
chunks, plus a runtime dep). Recorded follow-up trigger: if a single
locale's payload ever exceeds ~150 KB, split that locale per-namespace
lazily then.

### D5. Dependencies: `i18next` + `react-i18next`, pinned, nothing else

No `i18next-browser-languagedetector` (D3 decides), no
`i18next-http-backend` (D4 decides — `import.meta.glob` is Vite-native
lazy loading, no plugin, no runtime dependency). `bun add i18next
react-i18next` into `dependencies`; both work
with React 19 (`react-i18next` ≥ 15 has React 19 support in `peerDependencies`).

### D6. Test strategy: real i18n instance in setup, assertions stay readable

`vitest.setup.ts` imports the same `src/shared/i18n` instance (which
initialises from the real `en` locale JSONs, `en` active, missing-key
`parseMissingKeyHandler` left at default so dev warnings surface) — one
initialisation, no per-test wrapper, no mocked catalogues. Existing
`getByText("no projects yet")`-style assertions keep passing because
`en` is active and the locale file carries the same sentence the
literal used to. Tests that assert non-English renders (switcher test,
ru-render assertions) call `loadLocale(lng)` in their arrange — the
glob's dynamic imports resolve under jsdom with no network. What this
buys:

- a key typo or a missing namespace makes the component render the key
  path → text assertions fail loudly;
- a copy reword becomes a one-line JSON edit plus, only where a test
  pinned the old sentence, a deliberate test update — copy is spec;
- no test had to learn i18n mechanics.

A dedicated `src/shared/i18n/locale-parity.test.ts` walks every shipped
locale's resource tree recursively (locale list from the D13 registry)
and asserts key sets identical to the `en` source plus non-empty string
values (leaf objects allowed for plural groups). **Alternative
rejected:** asserting on keys (`getByText("projects.registry.title")`)
— unreadable in review and blind to the actual rendered word.

### D7. Non-component modules use the instance, not the hook

`relative-time.ts`, `permissions.ts`, `nav.ts`, label maps and toast
call sites cannot call `useTranslation`. They import the shared
instance (`i18n.t("common:relativeTime.minute", { count: 8 })`). Toast
copy moves to `t()` at the call site the same way. Pure helpers keep
their signatures (string in, string out) and localise one level up at
the map/table that owns the words — e.g. `needsLabel` returns
`i18n.t(...)` internally since its entire output is display words.

### D8. Missing-key behaviour is configured, not accidental

`fallbackLng: "en"`, and a `parseMissingKeyHandler` that returns the
full key path (`ns:key.sub`) — this is what makes the spec's
"renders the key path, never blank, never crash" scenario true by
construction. Dev (`import.meta.env.DEV`) additionally sets
`debug: true`, which logs `i18next::translator: missingKey`; prod
leaves `debug` off. `returnNull: false` guards against JSON `null`
values.

### D9. Rich fragments: `<Trans>` with component slots

The projects summary (`<span class=strong>3</span> projects · …`) and
similar styled copy use `react-i18next`'s `<Trans>` with the styled
spans passed as components/children slots; the JSON value reads
`"<count/> projects · <runs/> runs in flight · <spend/> today"`. Markup
stays in JSX, words stay in JSON. **Alternative rejected:** Interpolating
pre-wrapped HTML strings (`dangerouslySetInnerHTML`) — banned on
principle; **alternative rejected:** splitting into three separate
translated fragments — loses sentence order control, which Russian word
order needs.

### D10. Enforcement lands in `scripts/rule-audit.ts`

The repo already runs `bun run audit:fe` on `predev`/`prebuild` — the
natural gate. It gains a scan: in non-exempt `.tsx` (exempt:
`*.test.*`, `*.stories.*`, `_generated`, an explicit allowlist for
prose-bearing fixtures like mock seeds), flag JSX text nodes of 2+
letters and label-ish attributes (`label=`, `title=`, `description=`,
`placeholder=`, `summary=`, `emptyLabel=`, `aria-label=`, tooltip
`content=`) receiving a string literal instead of `t(...)`/`i18n.t(...)`.
Matches fail the audit with file + text. Keep the matcher conservative
(regex over source, same school as the existing audit) — false
negatives are acceptable, false positives are not; the parity test and
code review catch the rest.

### D11. Copy boundary pin (what never goes through `t()`)

- API `detail` strings — rendered verbatim inside localised chrome.
- LLM/worker-authored prose — step names, chat messages, knowledge and
  artifact content, memory digests.
- Mock seed *data* — project names, ticket titles, chat transcripts:
  fixtures standing in for server data.
- Dev logs, test names, `data-test`/`data-od-id` attributes, route
  paths, slugs, wire enum values (the enum→label *map* localises, the
  enum does not).

### D12. Provider placement and the switcher

`I18nextProvider` wraps inside `AppProviders` above `ThemeProvider`'s
children — outermost practical slot, so the Toaster's localised toast
copy and every screen share the instance. The instance initialises
synchronously with `en` (D4), so no loading state exists at boot. The
switcher is a registry-driven dropdown in the topbar beside
`theme-control` — a segmented control does not scale past two options —
listing every shipped locale by native endonym from the `LOCALES`
registry (D13), keyboard-accessible, `aria-label` from the `shell`
namespace; selecting a non-default locale triggers `loadLocale` then
`changeLanguage` (D4), with the control showing a brief pending state
while the chunk loads. `index.html` keeps `lang="en"` as the
pre-hydration default;
the provider syncs the attribute once the stored locale loads (same
first-paint posture as theme: the stored non-default locale may show
one English frame while its chunk loads — accepted, recorded here).

### D13. Shipped locale set: `en` default + `ru` + the OSS-popular six

The set (8 total): `en` (default, source of truth), `ru`, `de`, `es`,
`fr`, `pt-BR`, `zh-CN`, `ja` — mirroring what major open-source products
ship as their "popular locales" tier. Codes carry region tags where the
divergent regional variant is the one people expect (`pt-BR`, `zh-CN`).

A `LOCALES` registry constant in `src/shared/i18n/locales.ts` is the
single enumeration point: ordered entries `{ code, endonym, quality }`
driving the switcher (D12), the lazy loader (D4), the parity test's
locale list (D6) and the wave-0 skeleton scaffold. Endonyms are the
display names: English, Русский, Deutsch, Español, Français,
Português (Brasil), 简体中文, 日本語. Adding a locale later is one
registry entry plus its catalogues; removing one hides it everywhere at
once.

**Alternative rejected:** en+ru only, rest deferred — the owner decision
asks for the popular set up front («можем еще языки сразу добавить
популярные в opensource»); deferring re-opens the same planning cycle
after wave 0 for zero saved effort.

**Alternative rejected:** a longer tail (ko, it, pl, hi, ar, …) — no
owner signal for it; each locale adds a per-wave draft-generation and
parity surface; RTL (ar/he/fa) is a recorded non-goal of this change.

### D14. Authorship: EN source of truth, ru owner-reviewed, rest machine drafts

EN values are hand-authored alongside the keys and are the review
surface. RU values are written once per wave and reviewed by the owner
(native speaker) before the wave's gate. The other six locales (`de`,
`es`, `fr`, `pt-BR`, `zh-CN`, `ja`) are machine-generated per wave from
the EN catalogue and carry `quality: "draft"` in the `LOCALES` registry;
`ru` carries `"reviewed"`, `en` carries `"source"`. Policy, recorded:

- The parity test (D6) forces key-set identity and non-empty values
  across ALL locales — it never judges translation quality.
- Draft locales ship as-is: machine translation of a terse ops register
  is expected to be imperfect; the fallback chain (D8) and parity keep
  them structurally sound.
- Community contributions are the promotion path: a PR touching a
  locale's JSONs is the welcome vehicle; `draft` → `reviewed` is an
  owner action (a registry edit), not an agent action.
- Translation quality review is out of agent scope: agents generate and
  regenerate drafts; owners and contributors review them.
- The registry's `quality` field is metadata for tooling and docs, not
  chrome: the switcher renders only the endonym, with no draft badge —
  a dense ops surface does not gain a per-option quality mark (minimal
  marking, decided).

## Risks / Trade-offs

- **[Risk] Width: ~210 files touched** → waves with the full FE gate
  (`bun run typecheck && lint && test && build`) green after each;
  projects wave lands first and doubles as the pattern reference for
  the rest.
- **[Risk] Copy drift between waves** (half the app on `t()`, half
  literal) → intermediate state is accepted and visible; the literal
  ban (D10) switches from allowlist-everything to deny-by-default only
  in the final wave, so mid-migration builds stay green.
- **[Risk] Russian copy quality from a code-review-adjacent process** →
  ru values are written once per wave and reviewed by the owner (native
  speaker); terse ops register matches the product's existing voice
  (lowercase, figure-first).
- **[Risk] Machine-draft quality in six locales** → accepted and made
  explicit by policy (D14): drafts are flagged in the registry, parity
  keeps them structurally sound, the fallback chain keeps rendering
  correct; quality judgement is owner/community scope, never an agent
  claim.
- **[Risk] Parity-test surface grows 4× with the locale set** (one
  recursive walk per locale) → negligible: the walk is milliseconds over
  JSON trees, and it runs in the ordinary test suite, not per-test.
- **[Risk] `parseMissingKeyHandler` returning key paths could ship to
  prod visibly** → that is the pinned spec behaviour (honest failure
  over silent blank); the parity test makes the common case impossible
  and the fallback covers the rest.
- **[Risk] Tests pinning English sentences churn when copy is reworded**
  → accepted trade-off: copy is spec; the churn is one JSON line plus
  at most a test string.
- **[Trade-off] Lazy non-default locales: a first switch awaits a chunk
  load (brief pending state on the control)** → bought a one-locale
  main bundle and zero bytes for operators who never switch; revisit at
  the D4 per-locale threshold.

## Migration Plan

Wave 0 (infrastructure) ships the `LOCALES` registry, the i18n instance
with the lazy `loadLocale` path, skeleton catalogues for all eight
locales, provider, switcher, test setup, the cross-set parity test and
the audit check (audit in report-only allowlist
mode). Waves 1–6 migrate domains (see tasks.md ordering: projects →
shell/auth/home → runs/tasks/queue → identity/sources/approvals →
chat/knowledge/models/compute/cost/observability/verify/settings/
inbox/artifacts → shared kit + non-component helpers), each ending with
the full FE gate green; per wave, EN keys are authored and reviewed, ru
values are owner-reviewed, and the six draft locales are regenerated
from the wave's EN catalogue (D14). Final wave flips the audit to
deny-by-default
and removes the allowlist. Rollback at any wave boundary: revert the
wave's commits; the instance itself is inert if no component uses it
(wave 0 is independently revertible). No backend or data migration.

## Open Questions

None blocking. Recorded trigger conditions, not unknowns: D4's
per-locale split threshold; D3's default-language constant is one line
to flip if the owner later wants RU-first. One recorded confirmation,
not a blocker: machine-drafted locales (de/es/fr/pt-BR/zh-CN/ja) ship
`draft`-flagged with no reviewer on the path (D14) — if the owner wants
any of them human-reviewed before shipping, that is a per-locale owner
action after the wave lands.
