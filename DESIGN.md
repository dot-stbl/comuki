---
name: Comuki Dashboard
description: A dispatcher board for a swarm of coding agents — colourless chrome by construction, seven themes, colour only inside the flow.
colors:
  # The default theme — Dichromat deck, dark reading. Seven themes ship; this
  # snapshot is the committed default only. The source of truth is
  # dashboard/src/app/theme/themes.ts (registry) → themes.css (generated).
  floor: "#222226"
  lane: "#26262b"
  lane-alt: "#232327"
  rail: "#2b2b30"
  surface: "#26262b"
  surface-raised: "#313136"
  rule: "#37373c"
  rule-strong: "#47474c"
  text: "#e8e8ee"
  text-muted: "#b8b8bd"
  text-faint: "#8a8a8f"
  signal: "#ebebf0"
  status-running: "#8787f3"
  status-success: "#d7d7ff"
  status-failed: "#d2d228"
  status-waiting: "#b4b442"
  status-queued: "#a2a28a"
  status-escalated: "#b7b7fd"
typography:
  display:
    fontFamily: "Archivo Variable, Archivo, system-ui, sans-serif"
    fontSize: "1.375rem"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "-0.02em"
  title:
    fontFamily: "Archivo Variable, Archivo, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "normal"
  body:
    fontFamily: "Archivo Variable, Archivo, system-ui, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.55
    letterSpacing: "normal"
  data:
    fontFamily: "JetBrains Mono Variable, JetBrains Mono, ui-monospace, monospace"
    fontSize: "0.75rem"
    fontWeight: 500
    lineHeight: 1.2
    letterSpacing: "normal"
  data-small:
    fontFamily: "JetBrains Mono Variable, JetBrains Mono, ui-monospace, monospace"
    fontSize: "0.6875rem"
    fontWeight: 400
    lineHeight: 1.2
    letterSpacing: "normal"
  label-region:
    fontFamily: "JetBrains Mono Variable, JetBrains Mono, ui-monospace, monospace"
    fontSize: "0.625rem"
    fontWeight: 400
    lineHeight: 1.2
    letterSpacing: "0.08em"
  label-data:
    fontFamily: "Archivo Variable, Archivo, system-ui, sans-serif"
    fontSize: "0.625rem"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "0.02em"
rounded:
  hairline: "1px"
  xs: "3px"
  sm: "5px"
  md: "7px"
  lg: "10px"
spacing:
  s1: "0.125rem"
  s2: "0.3125rem"
  s3: "0.4375rem"
  s4: "0.625rem"
  s5: "0.8125rem"
  s6: "1rem"
  s8: "1.375rem"
components:
  button-default:
    backgroundColor: "{colors.signal}"
    textColor: "{colors.floor}"
    typography: "{typography.data}"
    rounded: "{rounded.md}"
    padding: "0 0.625rem"
    height: "1.75rem"
  button-outline:
    backgroundColor: "transparent"
    textColor: "{colors.text}"
    typography: "{typography.data}"
    rounded: "{rounded.md}"
    padding: "0 0.625rem"
    height: "1.75rem"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.text}"
    typography: "{typography.data}"
    rounded: "{rounded.md}"
    padding: "0 0.625rem"
    height: "1.75rem"
  button-destructive:
    backgroundColor: "color-mix(in oklab, #d2d200 15%, transparent)"
    textColor: "{colors.status-failed}"
    typography: "{typography.data}"
    rounded: "{rounded.md}"
    padding: "0 0.625rem"
    height: "1.75rem"
  button-sm:
    typography: "{typography.data-small}"
    rounded: "{rounded.md}"
    padding: "0 0.4375rem"
    height: "1.5rem"
  status-badge:
    backgroundColor: "color-mix(in oklab, #8787f3 10%, transparent)"
    textColor: "{colors.status-running}"
    typography: "{typography.data}"
    rounded: "{rounded.sm}"
    padding: "0.125rem 0.625rem"
  status-badge-failed:
    backgroundColor: "color-mix(in oklab, #d2d228 18%, transparent)"
    textColor: "{colors.status-failed}"
    typography: "{typography.data}"
    rounded: "{rounded.sm}"
    padding: "0.125rem 0.625rem"
  data-table-frame:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.lg}"
  data-table-header-cell:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-faint}"
    typography: "{typography.label-data}"
    padding: "0 0.625rem"
    height: "1.75rem"
  data-table-row:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    typography: "{typography.data-small}"
    padding: "0 0.625rem"
  data-table-row-selected:
    backgroundColor: "color-mix(in oklab, #8787f3 14%, transparent)"
    textColor: "{colors.text}"
  toolbar-input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    typography: "{typography.data-small}"
    rounded: "{rounded.md}"
    padding: "0 0.4375rem"
    height: "1.5rem"
  toolbar-select-trigger:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-muted}"
    typography: "{typography.data-small}"
    rounded: "{rounded.md}"
    padding: "0 0.4375rem"
    height: "1.5rem"
  dialog:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.lg}"
    width: "26rem"
---

# Design System: Comuki Dashboard

## Overview

**Creative North Star: "The Dispatcher's Board"**

This is a control room rendered as a screen. The floor is near-black and the chrome is deliberately drained of colour so that the only saturated thing in the viewport is the data itself: a flow of stages whose bands are coloured by run status. Everything else — rail, topbar, table, dialogs — is built from four greys and a hairline. The engineer on duty is not browsing; they are looking for where the swarm is jammed, and the composition is arranged so that question is answered before any individual run is.

Density is high but no longer airless. The first scale ran 2/5/7/10px and topped out at 1.375rem, and fifteen screens of it read as one undifferentiated mass — there was no step large enough to say *these two things are unrelated*. The scale is now 4/6/8/12/16/20/24/32 and the type ladder moved up a notch with it. Controls stand 1.75rem, table rows are one line and truncate rather than wrap, and every screen keeps a page gutter from the edge of the content region: content flush to the window edge reads as clipped rather than as full. Surfaces are not cards, but they are not squares either. The data table is a hairline-bounded frame carrying the largest corner in the scale and nothing else — no fill that lifts it off the floor, no shadow that floats it. The toolbar above it carries no border of its own; the split between board and list is a one-pixel rule the operator can drag.

The system carries two typefaces with a strict division of labour and two shadow planes, and it refuses to add a third of either. Colour never carries a status alone: every status band is hue *and* weave, so the reading survives greyscale without a second component — and since the theme system landed, it survives colour blindness by construction, not by accommodation. **Seven themes ship**, each with a dark and a light reading, mode chosen separately; the default is **Dichromat deck**, whose twenty colour primitives all lie on the plane linear-R = linear-G, which is a fixed point of both protan and deutan vision — run through either matrix, the same bytes come back, and a test asserts exactly that. The old Dispatcher palette survives as a selectable legacy theme, with a test that records (rather than hides) which of its statuses fail the contrast thresholds.

**Key Characteristics:**
- Colourless chrome by construction; saturation reserved for status inside the data
- Seven themes over one derived token layer — a new theme touches no component
- Two typographic voices — Archivo carries meaning, JetBrains Mono carries values
- Hairline-bounded data surfaces that take a corner but never a card's fill or shadow
- Exactly two in-page depth planes, both offset-and-blurred, never a halo
- Status is hue plus weave, consumed identically everywhere it appears
- Authored motion as moments, all opt-out: the river's two, the bar series' rise, the dialogs' paired entrance and exit
- Every chart stands beside a figure that says the reading in words

## Colors

A colourless chrome with the statuses built on the one axis red-green blindness leaves standing — the default theme is a fact of vision, not a style choice. All colour lives in two files that do not intersect: `themes.css` is **generated** (twenty-one colour primitives per theme, from the registry in `src/app/theme/themes.ts` — edit the registry, never the file; a test fails if they disagree), and `tokens.css` derives every semantic token the product reads from those primitives, once, so a new theme never has to know that `--sidebar-ring` exists.

### Themes
Seven in the registry: **Dichromat deck** (default; invariant under protan and deutan simulation by construction), Graphite, Dockside, Blueprint, Bureau, Aperture, and **Dispatcher** — the committed palette of the first year, kept selectable, its four failing status contrasts recorded by a test rather than excused. Each theme carries a dark and a light reading; the mode (dark / light / system) is a separate control beside the palette in the topbar, because the palette is chosen once and the mode flips with the room.

### Status ladder
The six statuses on the default theme are a lightness ladder with warm/cold alternation on the blue↔yellow axis — the axis dichromat vision preserves. `running` and `success` sit widest apart (ΔL\* 26) because they are the two statuses whose bands carry no weave — tone is their only code. `queued` sits nearest the zero-saturation point by the old design-system rule that it is the least saturated status; it and `waiting` share a side of the axis and are told apart by saturation and weave, which a test states explicitly.

### Muted text
`--text-faint` and `--text-muted` are **derived, not picked**: faint is the quietest step that still clears 4.5:1 on the theme's floor — that is its definition — and muted sits exactly midway between faint and the body text. Both mix along the theme's own floor→text ramp, so a palette on the invariant plane produces quiet text that stays on the invariant plane. Tests hold the ladder: gaps within 4 of each other, no adjacent step under 12, faint immediately above the threshold rather than comfortably above it (or it would be a second muted).

### Named Rules
**The Colourless Chrome Rule.** Rail, topbar, table chrome, dialogs and toolbars are built from the neutral ramp only. If a new surface wants a saturated fill, it is a data surface or it is wrong.

**The Two-Channel Status Rule.** A status is never hue alone. `.status[data-status="…"]` sets both `--hue` and `--weave`, and every consumer — the flow band, the legend swatch, the collapsed strip — reads those two properties from that one declaration. Adding a seventh status means adding a hatch, not just a colour.

**The Real Words Rule.** Status colours map one-to-one onto the product's six statuses: running, success, failed, waiting, queued, escalated. UI copy says "waiting on a human", never a coined synonym. Internal model names stay in the model and never surface as labels.

## Typography

**Interface Font:** Archivo Variable (with Archivo, system-ui, sans-serif)
**Data Font:** JetBrains Mono Variable (with JetBrains Mono, ui-monospace, monospace)

**Character:** Archivo is a grotesque with tight apertures that stays legible at 10px, which is the whole reason it can carry micro-labels at all. JetBrains Mono handles every number, identifier and control label — anything the operator compares column-wise. The pairing reads as instrumentation rather than editorial: nothing here is set to be admired, it is set to be scanned. `font-feature-settings: "zero"` is on globally, so a slashed zero separates `0` from `O` in run identifiers.

### Hierarchy
- **Display** (Archivo, 700, 1.375rem, line-height 1.05, tracking −0.02em): the screen title, and the pool figure on the marked stage — the one number allowed to grow.
- **Title** (Archivo, 600, 0.875rem, line-height 1.3): empty- and error-state headings. The same step, set in mono at 500, carries the resting pool figure on every unmarked stage.
- **Body** (Archivo, 400, 0.8125rem, line-height 1.55): the screen summary line, rail items, prose in error states — capped at 52ch where it runs as a paragraph.
- **Data** (JetBrains Mono, 500, 0.75rem, line-height 1.2): control labels, status badges, dialog copy.
- **Data Small** (JetBrains Mono, 400, 0.6875rem): table cells, filter inputs, popover options, run identifiers.
- **Region Label** (JetBrains Mono, 400, 0.625rem, tracking 0.08em, `--tracking-label`): the wide gesture. Rail group headings and section headings — it names a *region*.
- **Data Label** (Archivo, 600, 0.625rem, tracking 0.02em, `--tracking-data`): the tight gesture. Table column names, profile names, popover section titles — it names a *value*.

### Named Rules
**The Two Voices Rule.** Archivo carries meaning, JetBrains Mono carries values. `--font-mono` is an alias of `--font-data` so components ported from the old stack land on the right voice without being rewritten. A number in Archivo or a sentence in mono is a defect.

**The Two Trackings Rule.** Wide tracking (`--tracking-label`, 0.08em) marks a region heading; tight tracking (`--tracking-data`, 0.02em) marks a data label. They are not interchangeable and there is no third. Display type tightens (`--tracking-display`); nothing else sets tracking at all. Read the tokens, never these numbers — `tokens.css` is what ships.

**The No Capitals Rule.** Nothing in this product is set in uppercase — not a column name, not a rail heading, not an empty state, and never via `text-transform` or `toUpperCase()`. Capitals were how the old micro-label bought separation from the values beneath it; separation is now bought with weight, tracking and `--text-faint`, which is quieter and survives being read in Russian, where capitals are harder to scan. The two trackings still do the naming work they always did.

**The Tabular Figures Rule.** Any figure that sits in a column or updates in place carries `font-variant-numeric: tabular-nums` — table numerics, rail counts, meter readings, the flow's pool and mix lines. A scan down a column compares magnitudes, not glyph widths.

## Layout

The shell is a fixed full-height frame: a 13.5rem rail on the left, a 3rem topbar above the content, and a single scrolling main region that carries the page gutter on its inline axis. Nothing outside that region scrolls; `html` and `body` are `overflow: hidden`.

Spacing runs on a compressed eight-step scale from 0.125rem to 1.375rem, and the three tightest working steps (0.3125rem, 0.4375rem, 0.625rem) carry most of the load. Screen padding is 1rem; component internals sit between 0.4375rem and 0.8125rem.

The signature screen divides vertically with a draggable, collapsible split: the flow board on top, the runs table below, separated by a one-pixel rule with a generous invisible hit area. The split ratio persists per pane group in localStorage. The lower panel drives the table's scroll-port depth through a container query (`container-type: size` publishing `--h-table-body: 100cqh`), so rows always end exactly where the panel does at any divider position.

Two breakpoints, both about desks rather than phones. At **1240px** the flow's stage columns stop flexing and fix at 7.5rem, letting the board scroll sideways rather than crushing nine stages into slivers. At **1000px** the rail collapses to a 3rem icon strip — words go and the live counts survive as bare numerals beside their icons. The icons do *not* step up any more: rail items are large controls now and their icons already sit at the top of the scale, and holding one size across the collapse is what keeps the transition from jumping; the swarm meter drops its labels and keeps its bar, with the reading carried by `aria-label`.

### Named Rules
**The Shape-Not-Reading Rule.** When space runs out, a component either scrolls to keep its reading or shrinks to a shape. The flow board scrolls sideways; the collapsed stage strip shrinks, because it was never carrying numbers. Never crush a reading to fit.

## Elevation & Depth

The board has exactly two in-page planes, and only two. Depth is otherwise carried tonally — the rail sits darker than the floor, overlays sit lighter — and by hairline rules, not by shadow.

### Shadow Vocabulary
- **Lift** (`0 -10px 24px -12px` at 78% black): cast *upward* from the runs list onto the flow board behind it. It says the list is the nearer plane.
- **Header** (`0 6px 14px -10px` at 70% black): cast downward from a sticky table header onto the rows scrolling under it.
- **Modal** (two-layer, `0 14px 36px -10px` plus `0 3px 10px -4px`): reserved for things that leave the page plane entirely — the confirm dialog and toolbar popovers.

### Named Rules
**The Two Planes Rule.** Lift and Header are the whole vocabulary for in-page depth. A new surface that wants elevation is either in front of the flow (Lift) or in front of rows it scrolls over (Header). If it is neither, it is flat.

**The No-Halo Rule.** Every shadow has an offset and a blur. A zero-offset glow is a focus ring, never a depth cue — and the focus ring is a 2px `color-mix` ring on `--ring`, spelled the same way in every component.

## Shapes

Rectilinear and tight, and the corner grows with the box. Four steps, proportional rather than flat, with **no zero step**: **hairline** (1px) for the smallest marks, the split separator's own width and the 2px bars that have no room for a real corner; **xs** (3px) for meters, drawn channels, swatches, the switch thumb, a checkbox and an inline code span; **sm** (5px) for badges, chips, tags, menu and rail items and tooltips; **md** (7px) for buttons, text inputs and selects, popovers, notice bands and the flow's nodes; **lg** (10px) for the screen's own surfaces — the data-table frame, panels and dialogs.

The steps are optical, and they fail in both directions — which is why the step is chosen by the box rather than by the component's category. A small box wearing a large corner stops being a rectangle and reads as a capsule; a large box wearing a small corner reads as square whatever number is on it. A 10px corner on a 20px chip is wrong even though a chip is a control, and a 3px corner on a full-width panel is wrong even though a panel is a surface.

The scale ran 2 / 3.6 / 6px before, *and zero on every data surface*, which is exactly what it looked like: half the product rounded and half of it square. The 3.6px step was never a decision at all — it fell out of multiplying the shadcn bridge's `--radius` by 0.6 — so the four steps are literals in `tokens.css` now and the bridge is free to drift away from them.

Borders still do the work a shadow would do elsewhere: a 1px rule between rows, a 1px rule under the topbar, a 1px left border marking a failure notice and an active rail item. What changed is only that a bounded surface is now allowed to end in a corner.

The icon scale mirrors the type scale — 0.625 / 0.75 / 0.875 / 1rem — so an icon sits at the cap height of the type beside it and never above it. Kit controls size their own descendant SVGs from that scale per size variant; call sites never hand-size an icon.

### Named Rules
**The No-Card Rule.** A data surface is bounded by hairlines and takes the corner its size deserves; what it never takes is card chrome — a fill that lifts it off the floor and a shadow that floats it. Cards are for overlays.

The rule used to read *"bounded by hairlines, not wrapped in a rounded, shadowed container"*, and the word *rounded* in it did damage it was never meant to do. The point was always that data must not grow chrome. A corner is not chrome: it is a property of a box's size, the way a hairline is a property of a boundary, and rounding a frame brings back neither the fill nor the shadow. Read it as **hairline yes, corner yes, fill and shadow no**. The lane and surface colours are the material data is *made of* and are not the fill this rule forbids; a raised step, a gradient, or a `--shadow-modal` on a table is.

**The Clip-Not-Hidden Rule.** A rounded surface whose content reaches its edge is cut with `overflow: clip`, never `overflow: hidden`. `hidden` makes the box a scroll container, and `position: sticky` resolves against the nearest scrolling ancestor — so on the data table it would silently re-anchor every pinned cell from the scroll port up to the frame, and the pins would stop holding. `clip` cuts without opening a scroll port. Because jsdom lays nothing out and no rendered test can see a corner, `data-table.test.tsx` reads the stylesheet back off disk and fails if the frame ever acquires a scrolling overflow.

**The Icon-Rides-The-Control Rule.** An icon takes its size from the control it sits in. `button.module.css` sizes descendant SVGs per size variant; passing an explicit width to an icon inside a kit control is a defect.

## Components

### Buttons
- **Shape:** softened rectangle at the control step (7px) with a 1px transparent border, so variants can borrow the border without shifting layout. The form's own `.control` — text input and select — sits on the *same* step, because the two stand side by side in every form and used to disagree by three pixels in every one of them.
- **Size:** three heights — 1.5rem (sm, and no lower: 24px is WCAG 2.2's minimum target), 1.75rem (default), 2.125rem (lg) — plus a square icon-only variant at each. Labels are mono at 0.75rem/500 (0.6875rem at sm).
- **Default:** the accent fill on floor-dark text; hover mixes 15% of the text colour into the fill.
- **Outline / Secondary / Ghost:** transparent or muted fills bounded by the rule colour; hover deepens the fill rather than the border.
- **Destructive:** a 15% destructive wash with destructive text — never a solid red slab; hover deepens the wash to 25%.
- **Link:** auto height, no padding, underlined at 0.25em offset. Used when an anchor must look like a control; the exported `buttonClass()` recipe exists so the link takes the classes rather than nesting interactive elements.
- **Press / Focus:** a 1px downward nudge on active; a 2px 35% ring plus a solid ring-coloured border on focus-visible.

### Status Badge
- **Style:** inline-flex, sm radius, 1px rule border, a 10% wash of its own status hue, with an icon at the data icon size.
- **Semantics:** six variants matching the six statuses. `failed` is the one that escalates — 40% border and a full tint — because a failure should be visible before it is read.
- **Motion:** the `running` variant pulses opacity on a 1.4s loop, disabled under reduced motion.

### Data Table
- **Character:** a virtualized reading surface, not a container. Built on TanStack Table with a row virtualizer; the row height is published as `--dt-row-h` by the component, so the painted row and the computed offset are guaranteed to be the same number.
- **Shape:** the surface step (10px) on a frame bounded on all four sides by a strong hairline, with a 1px rule between rows. Still no card: no raised fill, no wrapping shadow. The corners are cut with `overflow: clip` — see the Clip-Not-Hidden Rule; `hidden` there brings the pinned-column bug back.
- **Header:** sticky, surface-coloured, carrying the Header shadow. Column names are set as data labels in faint text so they never compete with the values beneath.
- **Cells:** one line, truncating. Numeric columns switch to mono with tabular figures and may align to the end.
- **States:** rows tint to muted on hover; selection is a 14% the accent wash. Three densities (compact / default / comfortable) vary only the cell gutter and, for compact, the type step.
- **Empty:** a four-row-tall band, message centred as a mono micro-label.

### Data Table Toolbar
- **Character:** a bare bar directly above the table's top hairline, or in the header band when a screen hands it to `PageHeader`. One search field and one filter button on the left, the chips that say what is filtered beside them, table-level controls right, no border of its own. The search field and the button are a fixed left edge and never move: a control that walks away each time a filter goes on is a control that has to be re-found on every use. Chips grow to the right and wrap, so the row grows downward and never sideways.
- **Derivation:** filter controls are generated from each column's `meta.filter`, so a column declares its own filter and the toolbar assembles itself. The first `text` filter a column set declares is promoted to the row's search field — screens already write one text box that matches across several fields, and that *is* the search. Every other declared filter, including any further text filter, sits in the popover behind the button.
- **Controls:** 1.5rem-tall mono inputs and select triggers at the control step on the surface colour; hover strengthens the border and lifts the text from muted to full. They are inputs, so they take the same corner a button and a form field take — the filter chips beside them stay a step down, because a chip is furniture and not a control you type into.
- **Active filter:** a control that is doing something marks itself with its own border and its own text weight — 55% the accent mixed into the border, text at full strength. Never a coloured fill.
- **Filter chip:** one active filter, said out loud on the row so the reading survives without opening anything. A chip is a hairline-bounded rectangle at the sm step in the chrome's own material: transparent ground, a `--rule-strong` border, data-small mono text, and an `×` at the small icon size. The chip *is* the remove control for its own filter — one chip, one filter, one target — and its accessible name says which filter it drops, because "×" is not the name of anything. What a chip is **not**: not a pill (`--r-pill` stays forbidden and this is not the exception that reopens it), not a filled or tinted badge, not a coloured dot in any spelling. It carries no status hue and no wash — saturation stays inside the data, and the chip is chrome. A row with nothing active renders no chip strip at all rather than an empty one.
- **Count and chips:** the button reads `filters N`, where `N` is a bare mono tabular numeral inside the button's own label — part of the label, never a filled counter riding on it. It counts exactly the chips beside it: the count answers *how many*, the chips answer *which*, and they are one derivation read twice so they cannot drift. The promoted search is neither counted nor chipped — its value is already legible in the field it was typed into, and a chip repeating it would be the only chip that stood for nothing hidden.
- **Overlays:** popovers sit on the raised surface with the Modal shadow; options highlight on muted and read the accent when selected. The filter popover lays its fields out at most two to a line, each named above its control by a data label, and carries the one *clear all* out of the whole toolbar.

### Split Pane
- **Style:** the separator is a one-pixel rule at rest with a ±0.4375rem invisible hit area, so the drawn line stays a hairline while the grab target stays generous.
- **States:** hover, drag and focus turn the rule the accent; focus adds the standard 2px ring. A three-mark grip fades in only while the separator is live, so a resting board is a clean rule.
- **Persistence:** the component owns its own localStorage layout persistence, so a divider position survives reload per pane group.

### Confirm Dialog
- **Style:** 26rem wide, the surface step (10px), surface fill, 1px rule border, Modal shadow, over a 55%-black scrim. A dialog is one of the three boxes big enough for the largest corner; the buttons in its footer stay at the control step, so the dialog reads as the container and they read as what is in it.
- **Structure:** mono title at body size/600, mono body at data size in muted text, footer buttons right-aligned above a 1px top rule.
- **Motion:** the scrim fades and the modal rises 0.3125rem while scaling from 0.98, both on the 180ms standard ease — and every dialog now *leaves the way it arrived*: `data-exiting` keyframes run the entrance backwards while React Aria holds the mount for the animation's duration (skipping the wait where none runs, as in jsdom). A dialog that fades in and snaps out reads as crashed, not closed.

### Bottom Sheet
- **Character:** a modal panel docked to the bottom of the window, resizable by its top edge, able to fill it. The resize is the product's one implementation — a `SplitPane` group with a transparent measurement panel above the edge — so drag, keyboard resize, separator semantics and persisted depth are the same machinery the shell's rail and the runs board use.
- **Contract:** scrim, focus trap, escape closes, focus returns to the trigger. Non-dismissable by default (the form dialogs' rule); a `dismissable` prop turns the empty window above the sheet into a way out — right for a console whose state outlives the close, wrong for a form half-filled. The dismissal surface is the sheet's own transparent panel, not React Aria's gesture, because the panel sits inside the modal where the gesture cannot see it.
- **Motion:** the sheet rises from the edge it is docked to and leaves the same way, behind the reduced-motion guard.

### The Console (dock — second signature surface)
The console is one implementation (`ChatConsole`) in **two containers**: the `/chat` route (a page with a URL) and a floating dock — a trigger over the board's bottom-right corner (one gutter in from the content edge, one step above the floor — clear of the table's scrollbar and every other corner resident) opening the bottom sheet. The same-component rule is a test, not a hope: two implementations of the console are two paths into one journal, and the day they disagreed the operator would believe the wrong one.

- **The chord:** `ctrl/cmd + j` toggles the dock from anywhere — the terminal-panel key. The chord disarms without `chat.use`, the same rule as the trigger. `ctrl/cmd + k` is the global search.
- **The scrim's compensation:** a scrim hides the board, so opening the dock seeds the composer with what the location says the operator was looking at — an entity reference, removable in one gesture, its id riding the next message as text. A suggestion, never a mode.
- **State outside the tree:** the open sheet, the chosen conversation and the draft live in module memory, so a navigation (every screen mounts its own shell) neither closes the sheet nor drops a draft. Arrow-up in an empty box recalls the last thing said; the caret lands in the composer the moment the sheet opens.

### Time series (BarSeries, Sparkline)
- **Two primitives only, hand-drawn SVG:** a bar series on a shared day axis (geometry in percentages, so the corner token stays honest at any width; stacked segments may carry status tokens with their words in the legend) and a sparkline scaled 0→max — never min→max, because auto-range turns a quiet day into mountains.
- **The Chart-Beside-Figure Rule.** A chart never carries the reading alone: it stands beside a figure that says it in words, the precedent being the capacity card's ceiling sentence. A screen reader loses nothing; an operator scanning loses everything to a chart that must be decoded first.
- **Motion:** the bars rise from the baseline on entrance (620ms, 45ms stagger — the river's own moment), opt-out.

### Navigation (Rail and Topbar)
- **Rail:** eleven items in four groups — Intake (Inbox), Observe (Live runs, Queue, Approvals, Cost), Configure (Sources, Knowledge, Settings), Platform (Projects, Identity, Compute, Models). Region headings in the wide gesture, with the air *above* them — air below a heading ties it to its items, air above is what makes it a heading. Items stand at the large control height in Archivo body type, muted at rest, accent-filled on hover and active. The active item is marked by a docked accent seam painted with pseudo-elements, never by layout: the dock has to change nothing but colour, or switching items jitters the collapsed rail. Live counts ride at the end in mono micro figures; an alerting count takes the waiting hue — and a badge counts **its own screen's things**: the approvals row counts undecided approvals, not waiting runs.
- **Folds, not rows:** three screens earned no rail item of their own. Observability is the boards section of Compute; Verify is the gate tab of Knowledge; the console is the dock, not a section. The rule they follow — **the screen's permission gates the door, and a section folded in from another screen carries its own permission and hides below the door's** — hidden, never disabled, its query not even sent.
- **Topbar:** 2.75rem, rail-coloured, underlined by a 1px rule. The Comuki mark is the entire brand lockup — there is no wordmark — so it is built to read as a control: it takes `currentColor`, goes the accent on hover, and nudges 2px along its own axis (the container arrives and leaves; it does not swell). The nudge sits behind `prefers-reduced-motion: no-preference`; the colour shift does not. The bar's other residents: global search (`ctrl/cmd + k`), New run for shifts that may take intake, palette-before-mode, the repository link when one is configured.

### Comuki Mark
An inline SVG of a freight container, open on the loading side and marked on the face — the product's own object, since a worker container is torn down after one run. The viewBox is cropped to the artwork, so the element's box *is* the glyph: set a height and the width follows. Fill is `currentColor`; the mark never carries a colour of its own. Shipped rasters (`favicon.ico` at 16/32/48, `apple-touch-icon.png`, `icon-192`, `icon-512`, `icon-maskable-512`) place a light glyph on a solid Dispatcher Floor ground; `favicon.svg` adapts via `prefers-color-scheme`.

### Stage River (signature)
The screen's answer to "where is the swarm jammed". Nine stages across one shared vertical axis, forked into parallel lanes where the pipeline forks.

- **Channel and band:** each stage draws an empty Lane channel; the band inside it is scaled by how many runs *entered* that stage, so every channel is measured against the same scale. Within the band, segments stack by status, each painted with its hue and its weave. Cleared throughput reads as a dim ribbon.
- **Connectors:** a bar between two stages whose thickness is throughput only. A connector that narrows takes a 45% wash of the waiting hue.
- **Numbers:** two fixed-height lines under every channel — the pool figure, then that same pool split into the statuses it is actually made of, separated by middots. Fixed height, because every node must reserve the identical footprint or the channels stop sharing one axis.
- **The marked stage:** the stage holding the most blocked runs takes the waiting hue on its border, tint, label and a display-sized bold pool figure. A single reserved line under it reads `N waiting on a human` — reserved on every node, filled on one, because adding a line would shorten that stage's own channel.
- **Selection:** clicking a stage is an ordinary table filter. The pressed node strengthens its border and lightens its channel; the toolbar shows the same value a second way. There is no invisible coupling between board and list.
- **Legend:** sits beside the river and consumes the identical `data-status` declaration, so the weave is defined once and read once.
- **Collapsed:** the same flow, roughly one row tall — lanes and links, no numbers, no labels. A shape, not a reading.
- **Motion:** exactly two authored moments. The river fills from the source on entrance (620ms, 45ms stagger by column index), and a value that moved while nobody was looking washes once in the accent and leaves (900ms). Both sit behind `prefers-reduced-motion: no-preference`; the wash is driven by a shared `useValueChanged` hook that never fires on first render. The screen rebuild deliberately added no third.

## Do's and Don'ts

### Do:
- **Do** build new kit components as a triad — `.tsx` + `.module.css` + `.stories.tsx` — exported from `@/shared/ui`, with CSS Modules referencing only `var(--token)`.
- **Do** put a composite primitive (a table and its toolbar, a pane group and its separator) in its own folder with an `index.ts` re-exported from the root barrel. Flat files remain the rule for single components.
- **Do** give every status both a hue and a weave, and read them through `.status[data-status]` so the flow band, the legend and the collapsed strip can never drift apart.
- **Do** set every figure that sits in a column or updates in place in mono with tabular figures.
- **Do** bound data surfaces with hairlines (`--rule` between rows, `--rule-strong` at the edges) and give them the corner their size deserves, instead of wrapping them in a card's fill and shadow.
- **Do** let controls size their own icons from `--icon-xs` / `sm` / `md` / `lg`.
- **Do** put new motion behind `prefers-reduced-motion` and justify it as a *moment* — an entrance or a change — never as ambient decoration.
- **Do** write UI copy in the product's six real status words.
- **Do** say what a list is narrowed to on the row itself — one hairline chip per active filter, each removing its own — rather than making the operator open a popover to find out. In the chrome's material, never as a coloured pill.
- **Do** stand every chart beside a figure that says the reading in words — the chart confirms in shape, the figure carries the meaning.
- **Do** fold rather than add rows: a screen whose permission and clock match its host becomes that host's section or tab, gated by its own permission below the door's.

### Don't:
- **Don't** hardcode a hex, a px value or a font-family in a CSS Module. If the token is missing, add it to `tokens.css`; if it is a colour, it belongs to a theme in the registry, not to a component.
- **Don't** introduce a third in-page depth plane or a zero-offset glow. Lift, Header, Modal — and the focus ring is not a shadow.
- **Don't** use a coloured dot to carry meaning. The device was retired across the app; status reads as a badge, a band or a weave.
- **Don't** reach for `--r-pill`. It is a legacy step that no longer fits this form language; pills de-rounded to the sm step.
- **Don't** mix the two voices: no numbers in Archivo, no sentences in JetBrains Mono.
- **Don't** invent a third tracking role, or set tracking on anything that is neither a region heading, a data label, nor display type.
- **Don't** reach for a UI library. The shadcn quarantine was deleted whole — 175 files, recharts and all — and the kit is the only source of primitives there is.
- **Don't** put an internal model name into UI copy (`VirtualKey` stays in the model; the screen says spend keys).
- **Don't** couple two surfaces invisibly. If clicking one thing changes another, express it as a visible, resettable filter.

## Stated Gaps

Recorded as evidence rather than papered over. These are known incompletions the shipped build carries.

- ~~The strangler is mid-flight.~~ **Closed.** Every screen runs on the kit; the `_legacy/` quarantine, the shadcn showcase and recharts were deleted whole (175 files, 14 dependencies; the main chunk dropped 30%). There is no second UI stack to port from or to.
- ~~`--r-pill` still has one live consumer.~~ **Closed.** It stays defined, forbidden, and out of the four-step scale.
- **`--t-h1` remains defined-but-unused.** Its sibling `--s7` found work as the page gutter (`--page-x`); the headline step still has no consumer on any shipped surface. Recorded rather than documented as a role.
- ~~The confirm dialog's entrance is not opt-out.~~ **Closed.** Every motion moment — river, value wash, split grip, status pulse, dialog entrances and exits, the bar series' rise — sits behind the same `prefers-reduced-motion: no-preference` guard, and `responsive.test.ts` holds the whole of `src/app` and `src/shared` to it.
- **No server-side `stage × status` aggregate.** `buildStageFlow` derives the flow client-side from the run list because no endpoint returns a stage axis. Correct for the current page size; the shape in `stage-flow.ts` is what the endpoint must return.
- **The stage flow's node geometry is hand-computed.** `--h-node-head`, `--h-node-foot` and `--h-node-metrics` are local calcs that must stay in sync with the node's own chrome for connectors to start and stop where the channels do. It works; it is not a general layout mechanism.
- **The time series are seed-shaped, not server-shaped.** Four readings (spend by day, queue depth, run outcomes, proxy burn) run off day-series added to the mock seeds, with today's columns derived from live store values rather than invented. The backend contract those series imply is the next thing to write down.
- **Autonomy's live switch is still missing.** Its panel admits the gap in its own copy: the rows describe what autonomy *is*, the per-row control that would turn it is a feature the screen has not been given. The focused-toggle mutation pattern (the budget stops') is the shape it should take.
- **The console speaks a script, not a model.** Replies come from the chat seed; every tool, permission and hand-off is real machinery around a scripted brain, waiting for the lead model's endpoint.
