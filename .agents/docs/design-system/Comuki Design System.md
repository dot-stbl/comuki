# Comuki — Design System

A single reference for rebuilding the UI in this language. Feed this whole file to an
agent: it contains the philosophy, the exact tokens (both themes), the shadcn/ui
variable mapping, the component conventions, and how to theme. Values here match
`styles/tokens.css` (source of truth) and `styles/globals.css` (shadcn drop-in) 1:1.

---

## 1. Aesthetic

**Two voices, one accent.** Cool-ink neutrals with a single slate-blue brand
accent; Archivo carries meaning (headings, prose, labels), JetBrains Mono
Variable carries values (numbers, ids, machine text). Generous air,
hairline borders, ~6 px radius. Calm and exact — premium quality comes
from restraint, hairline borders, and spacing, never from gradients,
glow, or extra color.

Principles:
- **Two typefaces, each with a role.** Archivo = copy (headings, prose,
  labels). JetBrains Mono = values (numbers, ids, machine text). Do not
  mix them (no mono chrome, no sans prose).
- **Monochrome + one accent + danger.** The brand accent (slate-blue,
  `--primary`) is reserved for the primary action, focus rings, links,
  and the *active* status. Danger (terracotta, `--destructive`) is the
  only other loud color. Everything else lives on the cool-neutral ramp.
- **Status is carried by icon shape first, color second.** Statuses use
  muted, distinct hues so they read at a glance, but the icon always
  disambiguates (works in mono / for color-blind users).
- **Air over density.** Roomy padding, hairline (1px) borders, ~6px
  radius. Panels are quiet wells, not heavy cards. Soft shadows only.
- **Two themes, equally polished.** Dark is the default product theme;
  light is paper. Toggle with `.dark` on `<html>` (Tailwind v4
  convention).

Avoid: neon, gradient fills, emoji, heavy drop shadows, tight 2–3px
radii, multiple accent colors, decorative icons/stats that don't carry
information.

---

## 2. Type

Two voices: **Archivo** (sans, meaning-bearing copy — headings, prose, labels)
and **JetBrains Mono Variable** (mono, values — numbers, ids, machine text).
The old single-IBM-Plex-Mono system was retired; the sans carries the body, the
mono carries anything numeric, and the two don't bleed into each other.

| Family | Stack |
|---|---|
| `--font-ui` / `--font-sans` / `--font-heading` | `"Archivo Variable", "Archivo", system-ui, sans-serif` |
| `--font-data` / `--font-mono` / `--font-mono` | `"JetBrains Mono Variable", "JetBrains Mono", ui-monospace, monospace` |

Load (the kit owns both): `@import "@fontsource-variable/archivo";` and
`@import "@fontsource-variable/jetbrains-mono";`. Numbers/ids/machine text use
`font-mono` + `tabular-nums` so columns align.

### Scale (`--t-*`, in rem)

| Token | rem | px | Use |
|---|---|---|---|
| `--t-display` | 1.5 | 24 | hero / page H1 |
| `--t-h1` | 1.3125 | 21 | section titles |
| `--t-h2` | 1 | 16 | sub-titles |
| `--t-body` | 0.875 | 14 | body / default UI |
| `--t-sm` | 0.8125 | 13 | secondary |
| `--t-xs` | 0.75 | 12 | meta |
| `--t-micro` | 0.6875 | 11 | uppercase labels |

**Labels / eyebrows:** `--t-micro`, `--tracking-label: 0.08em`,
`text-transform: uppercase`, color `--text-faint`. Numbers/ids use
`font-feature-settings: "zero"` via `font-mono`.

---

## 3. Color tokens

Defined in `styles/tokens.css`. Dark is `:root` (default); light is `[data-theme="light"]`.

### Surfaces & text
| Token | Dark | Light | Role |
|---|---|---|---|
| `--bg` | `#15171B` | `#FBFBFA` | page floor |
| `--surface` | `#191C21` | `#FFFFFF` | panels / cards |
| `--surface-raised` | `#22262C` | `#FFFFFF` | elevated / popovers |
| `--surface-sunk` | `#1C1F25` | `#F3F4F3` | wells / inputs |
| `--nav-bg` | `#131519` | `#F6F7F6` | sidebar chrome |
| `--bar-bg` | `#17191E` | `#FCFCFB` | topbar chrome |
| `--border` | `#262A30` | `#E6E7E4` | hairline dividers |
| `--border-strong` | `#373C44` | `#D2D4D0` | emphasized / focus |
| `--text` | `#E8EAEE` | `#1B232E` | primary |
| `--text-muted` | `#969CA6` | `#697080` | secondary |
| `--text-faint` | `#5F656E` | `#A0A4AC` | tertiary / disabled |

### Accent & danger
| Token | Dark | Light | Role |
|---|---|---|---|
| `--accent` | `#83A1DC` | `#3C5A86` | brand · primary action · focus |
| `--accent-hover` | `#97B2E6` | `#32507A` | hover |
| `--accent-sunk` | `#6F8FD0` | `#2B466E` | active |
| `--on-accent` | `#0E1116` | `#FFFFFF` | text/icon on accent fill |
| `--danger` | `#D6685A` | `#B0473B` | destructive / failed |
| `--danger-hover` | `#E07A6D` | `#9E3F34` | |
| `--danger-sunk` | `#C55748` | `#8C372D` | |

> **Dark-mode gotcha:** in dark the accent is *light*, so text/icons on an accent fill
> must use `--on-accent` (dark), never hard-coded `#fff`. Same for checkmarks, switch
> knobs, primary-button labels, default badges.

### Status semantics (`--st-*`)
Muted distinct hues + icon. Only `running` (accent) and `failed` (danger) are "loud".

| Status | Dark | Light | Icon |
|---|---|---|---|
| `--st-running` | `var(--accent)` `#83A1DC` | `var(--accent)` `#3C5A86` | activity |
| `--st-success` | `#7BA68C` | `#4E7C5B` | check |
| `--st-failed` | `var(--danger)` `#D6685A` | `var(--danger)` `#B0473B` | x |
| `--st-waiting` | `#C6A35E` | `#9C7A3C` | clock |
| `--st-queued` | `#565C65` | `#A0A4AC` | circle (dashed/faint) |
| `--st-escalated` | `#9A86C7` | `#6E5BA6` | chevrons-up |

Tints: `--st-<name>-tint = color-mix(in oklab, var(--st-<name>) 18%, transparent)`.

---

## 4. Spacing, radius, depth, motion

**Spacing** (`--s*`, in rem — px @ 16): `--s1 0.25` (4) · `--s2 0.375` (6) ·
`--s3 0.5` (8) · `--s4 0.75` (12) · `--s5 1` (16) · `--s6 1.25` (20) ·
`--s7 1.5` (24) · `--s8 2` (32). Nothing below `--s1` exists; a gap smaller
than 4 px is a mistake, not a decision. Always lay out rows/groups with
flex/grid + `gap` (never bare inline + margins).

**Radius:** `--radius: 0.375rem` (~6 px). Tailwind bridge maps `--r-{xs,sm,md,lg}`
from the kit's own corner scale (`--r-xs` ... `--r-lg`).

**Depth** (surface + hairline border do most of the work; shadows are soft):
`--shadow-sm`, `--shadow-card`, `--shadow-lift`, `--shadow-pop`. Don't exceed
`--shadow-pop` for popovers/dialogs. `--shadow-pinned` for sticky rails
(`4px 0 8px 0` with a foreground tint).

**Motion:** `--ease: cubic-bezier(0.2,0.6,0.2,1)`, `--dur: 180ms`. No infinite decorative
loops; gate any motion behind `@media (prefers-reduced-motion: reduce)`. Action
icons inside buttons scale `1.08` on hover (120 ms ease-out) and `0.96` on press
(80 ms).

---

## 5. Tailwind v4 mapping

The dashboard wires its tokens through `dashboard/src/index.css`'s
`@theme inline` block. The Tailwind classes below resolve to the same
`var(--…)` the kit's own primitives use — `bg-background`, `text-foreground`,
`border-border`, etc. all hit the same hex/oklch values listed in §2–4.

| Tailwind / shadcn name | Comuki token | role |
|---|---|---|
| `bg-background` / `--background` | `--bg` (mapped `--background`) | app background |
| `text-foreground` / `--foreground` | `--text` | primary text |
| `bg-card` / `bg-popover` / `--card` / `--popover` | `--card` / `--popover` | panels, popovers |
| `bg-primary` / `--primary` | `--primary` (brand accent — slate-blue) | primary action, brand |
| `text-primary-foreground` / `--primary-foreground` | `--primary-foreground` | text on primary |
| `bg-secondary` / `--secondary` | `--secondary` | secondary buttons / quiet zones |
| `bg-muted` / `text-muted-foreground` | `--muted` / `--muted-foreground` | quiet zones / text |
| `bg-accent` / `--accent` | `--accent` (Tailwind shadcn slot — **neutral** hover surface, NOT the brand) | hover surface |
| `bg-destructive` / `--destructive` | `--destructive` | destructive / failed |
| `border-border` / `border-input` | `--border` / `--input` | borders & fields |
| `ring-ring` / `--ring` | `--ring` (slate-blue) | branded focus ring |
| `--radius` | `0.375rem` (~6 px) | softened, strict |
| `bg-st-running` / `bg-st-failed` etc. | `--st-running` / `--st-failed` | swarm/run statuses (icon carries) |

**To rebuild a shadcn component in this language:** keep shadcn's structure /
variants, just consume these variables. Filled `primary` = `--primary` bg +
`--primary-foreground` text; `secondary` / `outline` / `ghost` are quieter
(surface/transparent + `--border`); `destructive` = `--destructive`. Focus =
`outline:none; border-color:var(--ring); box-shadow: 0 0 0 3px color-mix(in oklab, var(--ring) 30%, transparent)`.
Radius via `--radius`. Archivo carries the body, JetBrains Mono carries the
values.

---

## 6. Component conventions

Reference implementation: `styles/components.css` (plain-CSS shadcn primitives) +
`Comuki shadcn Components.html` (live gallery). Class vocabulary:

- **Button** `.ui-btn` + `--secondary` `--outline` `--ghost` `--link` `--destructive`;
  sizes `--sm` `--lg` `--icon`. Height 32 (sm 28, lg 36), radius `--r-sm`, gap 6, icon 15px.
- **Input / Textarea / Select** `.ui-input` `.ui-textarea` `.ui-select` on `--surface-sunk`,
  1px `--border`; `.ui-field` (column + gap), `.ui-label`, `.ui-hint` (+`--err`).
- **Checkbox / Radio / Switch / Slider** `.ui-check` `.ui-radio` `.ui-switch` `.ui-slider`;
  checked fill `--accent`, mark color `--on-accent`.
- **Tabs** `.ui-tabs__list` / `.ui-tab` (segmented, sunk track).
- **Menu / Dropdown / Popover / Command** `.ui-menu` (`__label` `__item` `--danger` `__sep`),
  `.ui-command` (search + list). `.ui-pop` wrapper.
- **Breadcrumb** `.ui-breadcrumb` (mono, chevron `.sep`).
- **Pagination** `.ui-pagination` / `.ui-page`.
- **Card** `.ui-card` (`__h` `__title` `__desc` `__c` `__f`).
- **Dialog / Sheet** `.ui-overlay`(+`--sheet`) / `.ui-dialog` / `.ui-sheet`.
- **Tooltip** `.ui-tip` / `.ui-tip__c`.
- **Accordion** `.ui-acc` (`__item` `__trigger` `__panel`).
- **Table** `.ui-table` (mono uppercase `th`, hairline rows, `.mono` cells).
- **Badge** `.ui-badge` + `--default` `--secondary` `--outline` `--destructive`.
  **Status badge** `.badge[data-st="…"]` with a `.glyph[data-st]` icon (status palette §3).
- **Avatar / Progress / Skeleton** `.ui-avatar` · `.ui-progress`(`__bar`) · `.ui-skeleton`.
- **Alert** `.ui-alert` (+`--destructive`).
- **Toast** `.ui-toaster` / `.ui-toast` (+`--destructive`).

Icons: Lucide, 1.5–2px stroke, `currentColor`, sized 11–16px to context. The icon is the
primary carrier of status meaning.

---

## 7. Theming

- Theme switch: the dashboard uses Tailwind v4's `dark:` variant
  (`@custom-variant dark (&:is(.dark *))`). Toggle by setting
  `.dark` on `<html>` (or a wrapping element). All tokens are defined
  per-theme; components follow.
- New colors: don't invent. Pull from the tokens above. If you truly need an
  intermediate, derive with `color-mix(in oklab, …)` from an existing token.

---

## 8. Files in this project

| File | What |
|---|---|
| `dashboard/src/app/styles/themes.css` | Generated palette registry (per-theme primitives). Source of truth for colors. |
| `dashboard/src/app/styles/tokens.css` | **Token layer** — type, space, radius, shadow, motion tokens; derives everything else from `themes.css`. |
| `dashboard/src/index.css` | Tailwind v4 entry — `@theme inline` block maps tokens to Tailwind variables (`--color-*`, `--font-*`, `--radius-*`, `--text-*`). |
| `dashboard/src/shared/ui/primitives/*` | Custom Tailwind primitives (no shadcn). |
| `dashboard/src/shared/ui/apm/*` | APM-specific components (waterfall, log entry, duration, status pill). |
| `dashboard/stories/**` | Ladle catalog (live component gallery). |

---

## 9. Quick checklist for "rebuild a component / screen"

1. **Two voices** — Archivo for body / headings / labels, JetBrains Mono for
   numbers / ids / machine text. Don't bleed (no mono chrome, no sans prose).
2. **Slate-blue accent** (`--primary`) only for primary action / focus / links /
   active status; danger (`--destructive`) for destructive / failed.
3. **On accent fills** use `--primary-foreground` (which is dark in dark theme —
   the accent is light there, so don't hard-code `#fff`).
4. **1px hairlines** (`border-border`), `--radius` (~6 px), `--s*` spacing with
   flex/grid `gap`.
5. **Status = muted hue** (§3) + Lucide icon shape.
6. **Both themes** must read well — test light and dark.
7. **Soft shadows only**; motion 180 ms with `--ease`, reduced-motion safe.
8. **No raw hex / px in module CSS** — tokens only. `font-mono` for tabular
   numbers; `tabular-nums` for column-aligned figures.

---

## 10. Required input for an agent

To build anything in this system, an agent MUST load these three files
(they are the contract; this doc is the map):

1. `dashboard/src/app/styles/themes.css` — generated palette registry (per-theme
   primitives). Source of truth for colors. **Always load first.**
2. `dashboard/src/app/styles/tokens.css` — type, space, radius, shadow, motion
   tokens; derives from `themes.css`.
3. `dashboard/src/index.css` — Tailwind v4 entry; only when targeting the
   React dashboard (alternatives 1–2 alone are sufficient for raw HTML /
   Ladle stories).

Plain HTML page boot (Ladle stories):
```html
<html lang="…" class="dark">
  <link rel="stylesheet" href="/src/app/styles/themes.css">
  <link rel="stylesheet" href="/src/app/styles/tokens.css">
  <link rel="stylesheet" href="/src/index.css">
  <!-- Archivo + JetBrains Mono via @fontsource-variable (see §2) -->
```
Theme = `.dark` on `<html>` (Tailwind v4 convention).

---

## 11. Tokens as JSON (machine-readable)

```json
{
  "radius": { "default": "0.375rem" },
  "space":  { "s1":4,"s2":6,"s3":8,"s4":12,"s5":16,"s6":20,"s7":24,"s8":32 },
  "type":   { "display":24,"h1":21,"h2":16,"body":14,"sm":13,"xs":12,"micro":11,
              "uiFont":"Archivo Variable","dataFont":"JetBrains Mono Variable",
              "trackingLabel":"0.08em","trackingData":"0.02em","trackingDisplay":"-0.02em" },
  "motion": { "ease":"cubic-bezier(0.2,0.6,0.2,1)", "dur":"180ms" },
  "color": {
    "dark": {
      "bg":"#15171B","surface":"#191C21","surfaceRaised":"#22262C","surfaceSunk":"#1C1F25",
      "border":"#262A30","borderStrong":"#373C44",
      "text":"#E8EAEE","textMuted":"#969CA6","textFaint":"#5F656E",
      "primary":"#83A1DC","primaryForeground":"#0E1116",
      "danger":"#D6685A",
      "status":{ "running":"#83A1DC","success":"#7BA68C","failed":"#D6685A",
                 "waiting":"#C6A35E","queued":"#565C65","escalated":"#9A86C7" }
    },
    "light": {
      "bg":"#FBFBFA","surface":"#FFFFFF","surfaceRaised":"#FFFFFF","surfaceSunk":"#F3F4F3",
      "border":"#E6E7E4","borderStrong":"#D2D4D0",
      "text":"#1B232E","textMuted":"#697080","textFaint":"#A0A4AC",
      "primary":"#3C5A86","primaryForeground":"#FFFFFF",
      "danger":"#B0473B",
      "status":{ "running":"#3C5A86","success":"#4E7C5B","failed":"#B0473B",
                 "waiting":"#9C7A3C","queued":"#A0A4AC","escalated":"#6E5BA6" }
    }
  }
}
```

> The hex values above are stable across both the kit and the Tailwind v4
> bridge — see `dashboard/src/app/styles/themes.css` (generated from the theme
> registry) and the `@theme inline` block in `dashboard/src/index.css`.
> Color hues (slate-blue / cool-black / terracotta / muted status) are the
> same as §3; only the variable names changed (`--accent` → `--primary`,
> `--bg` → `--background`, etc.) to match Tailwind v4's slot names.

---

## 12. Component recipes (copy-paste)

Exact markup using `components.css`. Icons = inline Lucide `<svg class="ic">…</svg>` (§14).

**Buttons**
```html
<button class="ui-btn">Approve plan</button>
<button class="ui-btn ui-btn--secondary">Secondary</button>
<button class="ui-btn ui-btn--outline">Outline</button>
<button class="ui-btn ui-btn--ghost">Ghost</button>
<button class="ui-btn ui-btn--destructive">Cancel run</button>
<button class="ui-btn ui-btn--sm">Small</button>
<button class="ui-btn ui-btn--lg">Large</button>
<button class="ui-btn ui-btn--icon" aria-label="Add"><svg class="ic">…</svg></button>
```

**Field / input / select / textarea**
```html
<div class="ui-field">
  <label class="ui-label">Run id</label>
  <input class="ui-input mono" value="run_8f3c2a91">
  <span class="ui-hint">Markdown supported.</span>
</div>
<select class="ui-select"><option>production</option><option>staging</option></select>
<textarea class="ui-textarea" placeholder="Brief for the worker…"></textarea>
```

**Checkbox / radio / switch**
```html
<label class="ui-check"><input type="checkbox" checked> Auto-merge on green gate</label>
<label class="ui-radio"><input type="radio" name="esc" checked> Escalation: auto</label>
<label class="ui-switch"><input type="checkbox" checked> Real-time updates</label>
```

**Card**
```html
<div class="ui-card">
  <div class="ui-card__h">
    <div class="ui-card__title">billing-api · run_8f3c2a91</div>
    <div class="ui-card__desc">Idempotency for Stripe webhooks</div>
  </div>
  <div class="ui-card__c">…body…</div>
  <div class="ui-card__f"><button class="ui-btn ui-btn--sm">Trace</button></div>
</div>
```

**Badge & status badge** (status icon set in §3/§14)
```html
<span class="ui-badge ui-badge--default">Default</span>
<span class="ui-badge ui-badge--outline">Outline</span>
<span class="badge" data-st="running"><span class="glyph" data-st="running"></span>Running</span>
<span class="badge" data-st="failed"><span class="glyph" data-st="failed"></span>Failed</span>
```

**Table**
```html
<table class="ui-table">
  <thead><tr><th>run</th><th>app</th><th>status</th><th>cost</th></tr></thead>
  <tbody>
    <tr><td class="mono">8f3c2a91</td><td>billing-api</td>
        <td><span class="badge" data-st="running"><span class="glyph" data-st="running"></span>Running</span></td>
        <td class="mono">$0.42</td></tr>
  </tbody>
</table>
```

**Alert**
```html
<div class="ui-alert"><svg class="ic">…info…</svg>
  <div><div class="ui-alert__t">Baseline updated</div><div class="ui-alert__d">…</div></div></div>
<div class="ui-alert ui-alert--destructive"><svg class="ic">…alert…</svg>
  <div><div class="ui-alert__t">Red gate</div><div class="ui-alert__d">…</div></div></div>
```

**Tabs**
```html
<div class="ui-tabs">
  <div class="ui-tabs__list" role="tablist">
    <button class="ui-tab" role="tab" aria-selected="true">Live</button>
    <button class="ui-tab" role="tab" aria-selected="false">Trace</button>
  </div>
  <div class="ui-tabpanel" data-active>…</div>
</div>
```

**Dropdown / popover** (toggle `data-open` on `.ui-menu` with JS)
```html
<div class="ui-pop">
  <button class="ui-btn ui-btn--outline">Actions</button>
  <div class="ui-menu" data-open>
    <div class="ui-menu__label">Run</div>
    <div class="ui-menu__item"><svg class="ic">…</svg>Open trace</div>
    <div class="ui-menu__sep"></div>
    <div class="ui-menu__item ui-menu__item--danger"><svg class="ic">…</svg>Cancel</div>
  </div>
</div>
```

**Dialog** (toggle `data-open` on `.ui-overlay`)
```html
<div class="ui-overlay" data-open>
  <div class="ui-dialog">
    <div class="ui-dialog__h"><div class="ui-dialog__title">Approve plan?</div>
      <div class="ui-dialog__desc">8-stage DAG, 2 lanes. ~$0.40, ~6 min.</div></div>
    <div class="ui-dialog__f">
      <button class="ui-btn ui-btn--ghost">Cancel</button>
      <button class="ui-btn">Approve</button>
    </div>
  </div>
</div>
```

---

## 13. App shell & layout

Portable scaffold (tokens only — no product classes). Fixed sidebar + scrolling content.
```html
<div style="display:grid;grid-template-columns:232px 1fr;height:100vh">
  <aside style="background:var(--nav-bg);border-right:1px solid var(--border);padding:var(--s5) var(--s4);display:flex;flex-direction:column;gap:var(--s2)">…nav…</aside>
  <main style="overflow-y:auto;background:var(--bg)">
    <header style="position:sticky;top:0;background:var(--bar-bg);border-bottom:1px solid var(--border);padding:var(--s5) var(--s8)">…page head…</header>
    <div style="padding:var(--s6) var(--s8) var(--s10)">…screen body…</div>
  </main>
</div>
```
Rules: content max-width ~1180px for reading screens; cards grid =
`repeat(auto-fill, minmax(360px,1fr))` with `gap: var(--s5)`; two-column detail =
`grid-template-columns: 1fr 360px` (collapse to 1col under 900px). Page header pattern:
mono uppercase crumbs (`--t-micro`, `--text-faint`) → `--t-h1` title → `--t-sm` muted sub;
tools right-aligned. Section start = a number in `--accent` + `--t-h1` title + `--t-sm` sub.

---

## 14. Icons

Lucide, rendered inline as `<svg class="ic" viewBox="0 0 24 24">…paths…</svg>`; `.ic` sets
`stroke: currentColor; fill: none; stroke-width: 2; stroke-linecap/linejoin: round`. Size
via context (11–18px). The icon is the primary carrier of status meaning.

Status → icon: `running→activity` · `success→check` · `failed→x` · `waiting→clock` ·
`queued→circle` · `escalated→chevrons-up`. Common UI icons in use: `search, copy, eye,
settings, trash, plus, ellipsis, chevron-down/right/left, git-branch, git-commit, cpu,
timer, dollar-sign, triangle-alert, info, bell, rotate-ccw, sun, moon, lock, file,
terminal, server, book, box, flask, image, layers, database, grid, list`.

---

## 15. Interaction & accessibility contract

- **Focus:** every interactive control shows the branded ring —
  `outline:none; border-color:var(--accent); box-shadow:0 0 0 3px color-mix(in oklab, var(--accent) 30%, transparent)`.
  Never remove focus styling.
- **Hit targets:** ≥ 28px for dense controls; ≥ 44px on touch.
- **Disabled:** `opacity:.45; pointer-events:none`.
- **State coverage:** define hover, focus-visible, active, disabled for every control.
- **ARIA:** tabs (`role=tab/tablist`, `aria-selected`), menus toggle `data-open`,
  dialogs trap focus + close on Esc/backdrop, icon-only buttons need `aria-label`.
- **Copy/voice:** UI chrome is English and terse; lowercase mono micro-labels for
  meta/eyebrows. Only user/backend content (task titles, tickets) keeps its source language.
- **Motion:** transitions `var(--dur) var(--ease)`; gate non-essential motion behind
  `@media (prefers-reduced-motion: no-preference)`; no infinite decorative loops on content.

---

## 16. Golden screen (composition reference)

One full screen assembled from the system — the canonical example of rhythm, hierarchy,
and spacing. Live reference: `Comuki Dashboard.html` (Live runs). Skeleton:

```html
<div style="display:grid;grid-template-columns:232px 1fr;height:100vh">

  <!-- SIDEBAR: brand · grouped nav · status panel · footer -->
  <aside style="background:var(--nav-bg);border-right:1px solid var(--border);display:flex;flex-direction:column">
    <div style="display:flex;align-items:center;gap:var(--s3);padding:var(--s5) var(--s4);border-bottom:1px solid var(--border)">
      <span style="width:8px;height:8px;border-radius:2px;background:var(--accent)"></span>
      <b style="letter-spacing:.12em;font-size:13px">COMUKI</b>
    </div>
    <nav style="padding:var(--s4);display:flex;flex-direction:column;gap:2px">
      <span class="label" style="padding:0 8px var(--s2)">observe</span>
      <a class="nav__item nav__item--active">… Live runs</a>   <!-- active: bg var(--surface-raised) + 2px accent rail -->
      <a class="nav__item">… Approvals</a>
    </nav>
  </aside>

  <!-- MAIN: sticky page head → filter bar → card grid -->
  <main style="overflow-y:auto;background:var(--bg)">
    <header style="position:sticky;top:0;z-index:5;background:var(--bar-bg);border-bottom:1px solid var(--border);padding:var(--s5) var(--s8);display:flex;align-items:flex-end;gap:var(--s4)">
      <div>
        <nav class="label" style="display:flex;gap:5px;margin-bottom:5px">observe › live runs</nav>
        <h1 style="font-size:var(--t-h1);font-weight:600;margin:0">Live runs</h1>
        <div style="font-size:var(--t-xs);color:var(--text-muted);margin-top:3px">7 active · 18 total</div>
      </div>
      <div style="margin-left:auto;display:flex;gap:var(--s2)"><!-- tools: worker meter, view toggle --></div>
    </header>

    <div style="padding:var(--s6) var(--s8) var(--s10)">
      <!-- filter bar -->
      <div style="display:flex;gap:var(--s3);margin-bottom:var(--s5)">
        <label style="flex:1;display:flex;align-items:center;gap:var(--s2);height:30px;padding:0 var(--s3);background:var(--surface);border:1px solid var(--border);border-radius:var(--r-sm)">
          <svg class="ic">…search…</svg><input style="flex:1;background:none;border:0;outline:none;color:var(--text);font-family:var(--font-mono);font-size:12px" placeholder="Search…">
        </label>
        <select class="ui-select" style="width:auto;height:30px"><option>all apps</option></select>
      </div>
      <!-- card grid -->
      <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(360px,1fr));gap:var(--s5)">
        <div class="ui-card">… run card (see §12) …</div>
      </div>
    </div>
  </main>
</div>
```

What makes it read “expensive”: hairline borders everywhere, one accent (the active nav
rail + a single primary button), mono uppercase micro-labels for structure, generous
`--s5/--s8` padding, status carried by colored icon, soft `--shadow-card` on cards only.

---

## 17. Anti-patterns (do → don’t)

The live pages are the positive reference; these are the traps that break the language.

| Do | Don’t |
|---|---|
| `--r-sm`/`--r-card` (6–8px) | tight 2–3px corners (old console look) or pill-round 16px+ |
| accent only on primary / focus / link / active status | accent on secondary buttons, borders, headings, or as a fill behind text |
| text/icon `var(--on-accent)` on accent fills | hard-coded `#fff` on accent (invisible in dark — accent is light there) |
| status = muted hue **+** Lucide icon shape | status by color alone, or all-neutral gray “soup” (success vs waiting indistinguishable) |
| flat surfaces + 1px `--border` + soft `--shadow-card` | gradient fills, glassmorphism, heavy/colored drop shadows |
| one mono voice (IBM Plex Mono) | adding a serif display or a second sans “for contrast” |
| flex/grid + `gap` from `--s*` | bare inline-block siblings spaced by whitespace/margins |
| body ≥ 13px, labels 10–11px uppercase tracked | 8–9px body text, or sentence-case chrome |
| English UI chrome; native lang only for backend content | translating component labels / mixing languages in chrome |
| danger (terracotta) reserved for destructive/failed | red for emphasis, warnings, or “important” |
| icons as `currentColor` Lucide outlines, 1.5–2px | filled/duotone icons, emoji, or decorative icons with no meaning |
| motion 180ms `--ease`, reduced-motion safe | infinite pulses/spinners on content, long flashy transitions |
