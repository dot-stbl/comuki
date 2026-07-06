# Comuki — Design System

A single reference for rebuilding the UI in this language. Feed this whole file to an
agent: it contains the philosophy, the exact tokens (both themes), the shadcn/ui
variable mapping, the component conventions, and how to theme. Values here match
`styles/tokens.css` (source of truth) and `styles/globals.css` (shadcn drop-in) 1:1.

---

## 1. Aesthetic

**Premium monospace.** One typographic voice, cool-ink neutrals, a single slate-blue
brand accent, generous air, ~6px radius. Calm and exact — premiality comes from
restraint, hairline borders, and spacing, never from gradients, glow, or extra color.

Principles:
- **One typeface: IBM Plex Mono.** Headings, UI, body, and machine text are all mono.
  Do not introduce a serif or a second sans.
- **Monochrome + one accent + danger.** The brand accent (slate-blue) is reserved for
  the primary action, focus rings, links, and the *active* status. Danger (terracotta)
  is the only other loud color. Everything else lives on the cool-neutral ramp.
- **Status is carried by icon shape first, color second.** Statuses use muted, distinct
  hues so they read at a glance, but the icon always disambiguates (works in mono / for
  color-blind users).
- **Air over density.** Roomy padding, hairline (1px) borders, ~6px radius. Panels are
  quiet wells, not heavy cards. Soft shadows only.
- **Two themes, equally polished.** Dark is the default product theme; light is paper.
  Toggle with `data-theme="light" | "dark"` on `<html>`.

Avoid: neon, gradient fills, emoji, heavy drop shadows, tight 2–3px radii, multiple
accent colors, decorative icons/stats that don't carry information.

---

## 2. Type

| Family | Stack |
|---|---|
| All text (`--font-console` / `--font-ui` / `--font-display` / `--font-mono`) | `'IBM Plex Mono', Consolas, Menlo, 'DejaVu Sans Mono', ui-monospace, monospace` |

Load: `@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:ital,wght@0,400;0,500;0,600;0,700;1,400&display=swap');`
Enable `font-feature-settings: "ss01","zero"` on body.

### Scale (`--t-*`)
| Token | px | Use |
|---|---|---|
| `--t-display` | 28 | hero / page H1 |
| `--t-h1` | 20 | section titles |
| `--t-h2` | 15 | sub-titles |
| `--t-body` | 13 | body / default UI |
| `--t-sm` | 12 | secondary |
| `--t-xs` | 11 | meta |
| `--t-micro` | 10 | uppercase labels |

**Labels / eyebrows:** `--t-micro`, `letter-spacing: 0.12–0.16em`, `text-transform: uppercase`,
color `--text-faint`. Numbers/ids/machine text use the `.mono` helper (`"zero"` feature).

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

**Spacing** (`--s*`, roomy base): `--s1 2` · `--s2 5` · `--s3 7` · `--s4 10` · `--s5 13`
· `--s6 16` · `--s7 19` · `--s8 22` · `--s10 28` · `--s12 34` · `--s16 44` (px).
Always lay out rows/groups with flex/grid + `gap` (never bare inline + margins).

**Radius:** `--r-card 8px` · `--r-sm 6px` · `--r-pill 6px`.

**Depth** (surface + hairline border do most of the work; shadows are soft):
`--edge-hi` (1px lit top edge), `--shadow-sm`, `--shadow-card`, `--shadow-lift`,
`--shadow-pop`. Don't exceed `--shadow-pop` for popovers/dialogs.

**Motion:** `--ease: cubic-bezier(0.2,0.6,0.2,1)`, `--dur: 180ms`. No infinite decorative
loops; gate any motion behind `@media (prefers-reduced-motion: no-preference)`.

---

## 5. shadcn/ui mapping

Use `styles/globals.css` as the drop-in Tailwind v4 theme (it sets the variables below
in hex, both themes). Key idea: shadcn `--accent` is a **neutral hover surface**, NOT the
brand — the brand goes in `--primary`.

| shadcn variable | Comuki token | role |
|---|---|---|
| `--background` | `--bg` | app background |
| `--foreground` | `--text` | primary text |
| `--card` / `--popover` | `--surface` / `--surface-raised` | panels, popovers |
| `--primary` | `--accent` (slate-blue) | primary action, brand |
| `--primary-foreground` | `--on-accent` | text on primary |
| `--secondary` | `--surface-sunk` (raised in dark) | secondary buttons |
| `--muted` / `--muted-foreground` | `--surface-sunk` / `--text-muted` | quiet zones / text |
| `--accent` | `--surface-raised` | **neutral** hover surface (not brand!) |
| `--destructive` | `--danger` | destructive / failed |
| `--border` / `--input` | `--border` | borders & fields |
| `--ring` | `--accent` | branded focus ring |
| `--radius` | `0.375rem` (~6px) | softened, strict |
| `--status-*` (ext) | `--st-*` | swarm/run statuses (icon carries) |

**To rebuild a shadcn component in this language:** keep shadcn's structure/variants,
just consume these variables. Concretely: filled `primary` = `--primary` bg + `--primary-foreground`
text; `secondary`/`outline`/`ghost` are quieter (surface/transparent + `--border`);
`destructive` = `--destructive`. Focus = `outline:none; border-color:var(--ring);
box-shadow: 0 0 0 3px color-mix(in oklab, var(--ring) 30%, transparent)`. Radius via
`--radius`. Mono font everywhere.

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

- Theme switch: set `data-theme="dark"` (default) or `"light"` on `<html>`; persist in
  `localStorage` (key `comuki-theme`). All tokens are defined per-theme, components follow.
- For a real shadcn/Tailwind app: paste `styles/globals.css`, use `.dark` class (shadcn
  convention) — it carries the same dark values.
- New colors: don't invent. Pull from the tokens above. If you truly need an intermediate,
  derive with `color-mix(in oklab, …)` from an existing token.

---

## 8. Files in this project

| File | What |
|---|---|
| `styles/tokens.css` | **Source of truth** — all color/type/space/radius/shadow tokens, both themes, plus base reset & shared primitives (`.badge`, `.chip`, `.btn`, `.iconbtn`, `.label`, `.ic`). |
| `styles/components.css` | shadcn/ui primitives recreated in this language (`.ui-*`). |
| `styles/globals.css` | Drop-in Tailwind v4 theme for a real shadcn project (hex, both themes, `@theme inline`). |
| `Comuki Foundation.html` | Home / examples — tokens, type, color, status, philosophy. |
| `Comuki shadcn Components.html` | Live gallery of every component + the shadcn mapping table. |
| `Comuki Dashboard.html` (+ `dashboard/`) | Full product mock — live runs, stage pipeline + per-stage inspector, approvals, cost, settings. |

---

## 9. Quick checklist for "rebuild a component / screen"

1. Mono everywhere; sizes from the type scale (min 13px body in UI).
2. Slate-blue accent only for primary/focus/links/active status; danger for destructive/failed.
3. On accent fills use `--on-accent` (not `#fff`).
4. 1px `--border` hairlines, `--r-sm`/`--r-card` radii, `--s*` spacing with flex/grid `gap`.
5. Status = muted hue (§3) + Lucide icon shape.
6. Both themes must read well — test light and dark.
7. Soft shadows only; motion 180ms, reduced-motion safe.

---

## 10. Required input for an agent

To build anything in this system, an agent MUST load these three files (they are the
contract; this doc is the map):

1. `styles/tokens.css` — the variables + base reset + shared primitives. **Always link first.**
2. `styles/components.css` — the `.ui-*` component classes (depends on tokens).
3. `styles/globals.css` — only when targeting a real shadcn/Tailwind v4 app (alternative to 1–2).

Plain HTML page boot:
```html
<html lang="…" data-theme="dark">
  <link rel="stylesheet" href="styles/tokens.css">
  <link rel="stylesheet" href="styles/components.css">
  <!-- IBM Plex Mono via Google Fonts (see §2) -->
```
Theme = `data-theme="dark|light"` on `<html>`, persisted in `localStorage["comuki-theme"]`.

---

## 11. Tokens as JSON (machine-readable)

```json
{
  "radius": { "card": "8px", "sm": "6px", "pill": "6px" },
  "space":  { "s1":2,"s2":5,"s3":7,"s4":10,"s5":13,"s6":16,"s7":19,"s8":22,"s10":28,"s12":34,"s16":44 },
  "type":   { "display":28,"h1":20,"h2":15,"body":13,"sm":12,"xs":11,"micro":10, "font":"IBM Plex Mono" },
  "motion": { "ease":"cubic-bezier(0.2,0.6,0.2,1)", "dur":"180ms" },
  "color": {
    "dark": {
      "bg":"#15171B","surface":"#191C21","surfaceRaised":"#22262C","surfaceSunk":"#1C1F25",
      "navBg":"#131519","barBg":"#17191E","border":"#262A30","borderStrong":"#373C44",
      "text":"#E8EAEE","textMuted":"#969CA6","textFaint":"#5F656E",
      "accent":"#83A1DC","accentHover":"#97B2E6","onAccent":"#0E1116",
      "danger":"#D6685A",
      "status":{ "running":"#83A1DC","success":"#7BA68C","failed":"#D6685A","waiting":"#C6A35E","queued":"#565C65","escalated":"#9A86C7" }
    },
    "light": {
      "bg":"#FBFBFA","surface":"#FFFFFF","surfaceRaised":"#FFFFFF","surfaceSunk":"#F3F4F3",
      "navBg":"#F6F7F6","barBg":"#FCFCFB","border":"#E6E7E4","borderStrong":"#D2D4D0",
      "text":"#1B232E","textMuted":"#697080","textFaint":"#A0A4AC",
      "accent":"#3C5A86","accentHover":"#32507A","onAccent":"#FFFFFF",
      "danger":"#B0473B",
      "status":{ "running":"#3C5A86","success":"#4E7C5B","failed":"#B0473B","waiting":"#9C7A3C","queued":"#A0A4AC","escalated":"#6E5BA6" }
    }
  }
}
```

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
