# Dashboard i18n — Delta

## Purpose

All user-facing dashboard copy renders through one translation layer with
English as the default and a fixed set of additional locales shipped
alongside it (`ru`, `de`, `es`, `fr`, `pt-BR`, `zh-CN`, `ja`), so the
operator can read the board in any shipped language without a code change.

## ADDED Requirements

### Requirement: All user-facing copy renders through the i18n layer

Every piece of user-facing copy the dashboard renders SHALL resolve
through the translation layer in every shipped locale: JSX text, control
labels, tooltips, placeholders, summaries, empty-state strings, error-state
fallback strings, toast messages, breadcrumb and navigation labels,
command-palette entries, accessibility labels (`aria-label`, accessible
names), and the word maps non-component modules derive for display
(permissions denial labels, relative-time units, status word maps).
Test identifiers (`data-test` and kin) SHALL NOT localise.

#### Scenario: Registry renders in Russian

- **WHEN** the active locale is `ru` and the projects registry renders
- **THEN** its title, summary line, toolbar labels, empty states and
  action tooltips are Russian text, and no English chrome string remains
  on the screen

#### Scenario: English is the default

- **WHEN** the dashboard boots for an operator with no stored locale
  choice
- **THEN** the active locale is `en`

#### Scenario: Accessibility labels follow the locale

- **WHEN** the active locale is `ru` and a screen renders an icon-only
  control with an accessible name
- **THEN** the accessible name is the Russian translation of that
  control's label

### Requirement: Locale parity

Every shipped locale's resources SHALL carry a key set identical to the
English source of truth: every translation key referenced by the dashboard
SHALL exist in every shipped locale's resources, and no locale's resources
SHALL carry keys the English source lacks. Parity SHALL be enforced by an
automated check in the ordinary test run across the full locale set.

#### Scenario: Parity check fails on an asymmetric key

- **WHEN** a key exists in the English resources but is missing from one
  of the other shipped locales — or a locale carries a key the English
  source lacks — and the test suite runs
- **THEN** the parity check fails, naming the offending locale, namespace
  and key

#### Scenario: Empty translation refused

- **WHEN** a locale resource carries a key whose value is an empty
  string and the parity check runs
- **THEN** the check fails for that key

### Requirement: Fallback and missing-key behaviour

The translation layer SHALL fall back to English for a key missing from
the active locale's resources (fallback chain: active locale → `en`).
For a key missing from every loaded
resource the layer SHALL render the key path itself — never a blank, and
never a crash — in both development and production builds. In
development builds a missing key SHALL additionally be reported to the
console; in production builds it SHALL fail silently apart from the
rendered key path.

#### Scenario: Missing non-English key falls back to English

- **WHEN** the active locale is any shipped non-English locale (e.g.
  `ru`) and a copy key has an `en` value but no value in the active
  locale
- **THEN** the screen renders the English value

#### Scenario: Unknown key does not blank the screen

- **WHEN** a component references a key that exists in no loaded
  resource
- **THEN** the component renders the key path as visible text and the
  dashboard keeps functioning

#### Scenario: Development warns, production does not

- **WHEN** a missing key is hit in a development build (`dev`, `dev:mock`
  modes) and the same hit occurs in a production build
- **THEN** the development console carries a missing-key entry naming
  the key, and the production console does not

### Requirement: Pluralisation and interpolation

Copy that carries counts or named values SHALL use the translation
layer's pluralisation and interpolation rather than string concatenation,
with locale-correct plural forms (e.g. the Russian one/few/many/other;
single-form locales such as `ja` and `zh-CN` collapse to one form).
Values embedded
in copy SHALL be escaped by default; copy carrying styled fragments
SHALL keep markup out of the locale resources by composing the
translation text with placeholder slots for the styled parts.

#### Scenario: Russian plural forms for a count

- **WHEN** the active locale is `ru` and a summary renders "1 project",
  "2 projects", "5 projects" equivalents
- **THEN** the three render with the correct Russian plural forms
  (один проект / 2 проекта / 5 проектов), each count value interpolated

#### Scenario: Styled summary survives translation

- **WHEN** the registry summary renders counts emphasised by a strong
  style in either locale
- **THEN** the emphasised spans wrap the figures and the surrounding
  words come from the translation value, with no markup stored in the
  locale resource

### Requirement: Locale choice is persistent and switchable

The dashboard SHALL offer a locale control in the shell that enumerates
every shipped locale — each labelled with its native endonym (English,
Русский, Deutsch, Español, Français, Português (Brasil), 简体中文, 日本語)
and no other option — placed with the theme control. The chosen locale
SHALL persist
across visits in browser local storage, and the document's `lang`
attribute SHALL reflect the active locale at all times. Switching SHALL
take effect without a page reload; the default locale SHALL be available
at boot without any asynchronous locale load.

#### Scenario: Switcher enumerates the shipped set

- **WHEN** the locale control opens
- **THEN** it offers exactly the shipped locales, each named by its
  native endonym

#### Scenario: Switch to Russian persists

- **WHEN** the operator switches the locale control to Russian and
  reloads the dashboard
- **THEN** the dashboard boots in Russian

#### Scenario: Default boot needs no locale fetch

- **WHEN** the dashboard boots for an operator with no stored locale
  choice
- **THEN** the interface renders English immediately, with no non-default
  locale resource loading in flight

#### Scenario: Document language follows the locale

- **WHEN** the active locale is `ru`
- **THEN** the root document element carries `lang="ru"`; it carries
  `lang="en"` under the English locale and `lang="zh-CN"` under the
  Chinese (Simplified) locale

### Requirement: Copy boundary — pass-through strings

Strings that originate outside the dashboard's own copy SHALL pass
through untranslated: error detail delivered by the API (the localised
fallback around it still renders from the locale resources), content
authored by models and workers (run step names, chat messages,
knowledge and artifact content), and mock seed data standing in for
server data. The dashboard's own chrome around all of the above SHALL
still localise.

#### Scenario: API error detail passes through

- **WHEN** a request fails and the API returns an English detail string
  while the active locale is `ru`
- **THEN** the error surface renders the API's detail string verbatim
  inside Russian chrome (title and retry label from the locale
  resources)

#### Scenario: Model-authored step name is not translated

- **WHEN** the active locale is `ru` and a run detail renders work-item
  step names written by a model
- **THEN** the step names render verbatim; the labels, headings and
  statuses around them render in Russian

### Requirement: Hardcoded-copy ban is enforced

The dashboard's rule audit SHALL fail when a non-exempt source file
introduces user-facing copy as a literal in JSX (text nodes or
label-bearing attributes) instead of a translation call, with exemptions
for test files, story files, generated code and a reviewed allowlist.
The audit runs in the ordinary pre-build path, so a literal-copy
regression fails before the dashboard builds.

#### Scenario: Literal JSX text fails the audit

- **WHEN** a component renders a text node of English words outside a
  translation call and the rule audit runs
- **THEN** the audit fails, naming the file and the offending text
