/**
 * UI copy resource for the OpenTUI spike.
 *
 * i18next is the *user-facing* copy surface. The Core chat shell and
 * the command palette consult this module for every label and
 * description. Command **identity** (the `name` field in
 * `BUILTIN_COMMANDS`) is *not* translated — it stays a stable
 * programming handle, so the keymap dispatch can route by name
 * regardless of locale.
 *
 * The spike ships two parallel resources: `en` (default) and `ru`.
 * `LOCALES` enumerates them; `REQUIRED_KEYS` is the closed set the
 * spike's UI uses. A test in `tests/i18n.test.ts` asserts that every
 * required key is present in every locale, in both languages.
 */
export type LocaleCode = "en" | "ru"

export const LOCALES: readonly LocaleCode[] = ["en", "ru"] as const

export const DEFAULT_LOCALE: LocaleCode = "en"

export type LocaleResource = Readonly<Record<string, string>>

export const EN: LocaleResource = {
  "chrome.title": "  comuki · opentui-spike (core) · focus-mode",
  "chrome.titleCompact": " comuki·opentui-spike",
  "composer.placeholder": "Ask Comuki. Use / for actions or @ for knowledge.",
  "composer.placeholderCompact": "ask ›",
  "approval.action.approve": "approve",
  "approval.action.reject": "reject",
  "approval.intentPrefix": "REACT-APPROVAL-INTENT:",
  "approval.scopePrefix": "REACT-APPROVAL-SCOPE:",
  "approval.riskPrefix": "REACT-APPROVAL-RISK:",
  "approval.planPrefix": "REACT-APPROVAL-PLAN:",
  "approval.stepPrefix": "REACT-APPROVAL-STEP:",
  "approval.diffPrefix": "REACT-APPROVAL-DIFF:",
  "approval.decideLabel": "  decide:",
  "cmd.open-palette.label": "Open command palette",
  "cmd.open-palette.description": "Open the command palette (same registry as keystrokes).",
  "cmd.close-palette.label": "Close command palette",
  "cmd.close-palette.description": "Dismiss the palette overlay.",
  "cmd.save-snippet.label": "Save composer draft as snippet",
  "cmd.save-snippet.description": "Persist the composer text into the snippet store.",
  "cmd.approve-plan.label": "Approve pending plan",
  "cmd.approve-plan.description": "Send an approve decision for the awaiting-approval turn.",
  "cmd.reject-plan.label": "Reject pending plan",
  "cmd.reject-plan.description": "Send a reject decision for the awaiting-approval turn.",
  "cmd.queue-followup.label": "Queue follow-up message",
  "cmd.queue-followup.description": "Submit and queue a follow-up while the current turn runs.",
  "cmd.submit-turn.label": "Submit turn",
  "cmd.submit-turn.description": "Plain Enter submits the composer.",
  "cmd.copy-last-answer.label": "Copy last assistant answer",
  "cmd.copy-last-answer.description": "OSC 52 write of the most recent assistant text.",
  "cmd.open-status.label": "Open status panel",
  "cmd.open-status.description": "Toggle the status overlay over the transcript viewport.",
} as const

export const RU: LocaleResource = {
  "chrome.title": "  comuki · opentui-spike (core) · focus-mode",
  "chrome.titleCompact": " comuki·opentui-spike",
  "composer.placeholder": "Спроси Comuki. / для действий, @ для знаний.",
  "composer.placeholderCompact": "спросить ›",
  "approval.action.approve": "одобрить",
  "approval.action.reject": "отклонить",
  "approval.intentPrefix": "REACT-APPROVAL-INTENT:",
  "approval.scopePrefix": "REACT-APPROVAL-SCOPE:",
  "approval.riskPrefix": "REACT-APPROVAL-RISK:",
  "approval.planPrefix": "REACT-APPROVAL-PLAN:",
  "approval.stepPrefix": "REACT-APPROVAL-STEP:",
  "approval.diffPrefix": "REACT-APPROVAL-DIFF:",
  "approval.decideLabel": "  решай:",
  "cmd.open-palette.label": "Открыть палитру команд",
  "cmd.open-palette.description": "Открыть палитру команд (тот же реестр, что и клавиатура).",
  "cmd.close-palette.label": "Закрыть палитру команд",
  "cmd.close-palette.description": "Скрыть оверлей палитры.",
  "cmd.save-snippet.label": "Сохранить черновик как сниппет",
  "cmd.save-snippet.description": "Сохранить текст черновика в хранилище сниппетов.",
  "cmd.approve-plan.label": "Одобрить ожидающий план",
  "cmd.approve-plan.description": "Отправить решение «одобрить» для ожидающего хода.",
  "cmd.reject-plan.label": "Отклонить ожидающий план",
  "cmd.reject-plan.description": "Отправить решение «отклонить» для ожидающего хода.",
  "cmd.queue-followup.label": "Поставить follow-up в очередь",
  "cmd.queue-followup.description": "Отправить и поставить follow-up в очередь во время текущего хода.",
  "cmd.submit-turn.label": "Отправить ход",
  "cmd.submit-turn.description": "Обычный Enter отправляет черновик.",
  "cmd.copy-last-answer.label": "Скопировать последний ответ ассистента",
  "cmd.copy-last-answer.description": "Запись в буфер обмена через OSC 52.",
  "cmd.open-status.label": "Открыть панель статуса",
  "cmd.open-status.description": "Переключить оверлей статуса над transcript-окном.",
} as const

export const RESOURCES: Readonly<Record<LocaleCode, LocaleResource>> = {
  en: EN,
  ru: RU,
} as const

export const REQUIRED_KEYS: readonly string[] = [
  "chrome.title",
  "chrome.titleCompact",
  "composer.placeholder",
  "composer.placeholderCompact",
  "approval.action.approve",
  "approval.action.reject",
  "approval.intentPrefix",
  "approval.scopePrefix",
  "approval.riskPrefix",
  "approval.planPrefix",
  "approval.stepPrefix",
  "approval.diffPrefix",
  "approval.decideLabel",
  "cmd.open-palette.label",
  "cmd.open-palette.description",
  "cmd.close-palette.label",
  "cmd.close-palette.description",
  "cmd.save-snippet.label",
  "cmd.save-snippet.description",
  "cmd.approve-plan.label",
  "cmd.approve-plan.description",
  "cmd.reject-plan.label",
  "cmd.reject-plan.description",
  "cmd.queue-followup.label",
  "cmd.queue-followup.description",
  "cmd.submit-turn.label",
  "cmd.submit-turn.description",
  "cmd.copy-last-answer.label",
  "cmd.copy-last-answer.description",
  "cmd.open-status.label",
  "cmd.open-status.description",
] as const

/**
 * Minimal i18next-style lookup. The spike's chat shell and command
 * palette both consult this — the key is a dotted path inside a
 * locale resource. The English (or default) resource is the source
 * of truth, and a missing key raises a visible mismatch so the
 * tests in `tests/i18n.test.ts` can catch it at boot time.
 */
export function t(key: string, locale: LocaleCode = DEFAULT_LOCALE): string {
  const resource = RESOURCES[locale]
  const fallback = RESOURCES[DEFAULT_LOCALE]
  const value = resource[key] ?? fallback[key]
  if (value === undefined) {
    throw new Error(
      `i18n: missing key '${key}' in both '${locale}' and default locale '${DEFAULT_LOCALE}'`
    )
  }
  return value
}

/** Type-narrowed lookup for keys that must exist in every locale. */
export function tr<K extends string>(
  key: K,
  locale: LocaleCode = DEFAULT_LOCALE
): string {
  return t(key, locale)
}
