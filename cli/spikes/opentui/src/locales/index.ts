/**
 * i18next integration for the OpenTUI spike.
 *
 * `createI18nFor(locale)` returns a fully-initialized i18next
 * instance (`Promise<I18nInstance>`) configured for the spike's
 * `spike` namespace, with two parallel resources (en + ru). The
 * spike's helpers (`tr`) read from any initialized `i18n`
 * instance.
 *
 * Command **identity** (`BUILTIN_COMMANDS[].name`) is *not*
 * translated — it remains a stable programming handle, so the
 * keymap dispatches by name regardless of locale. The locale
 * resource only carries user-facing label / description /
 * placeholder / approval-card copy.
 */
import i18next, { type i18n } from "i18next"

export type I18nInstance = i18n

export type LocaleCode = "en" | "ru"

export const LOCALES: readonly LocaleCode[] = ["en", "ru"] as const

export const DEFAULT_LOCALE: LocaleCode = "en"

export const SPIKE_NAMESPACE = "spike" as const

/**
 * The closed set of dotted keys the spike's UI uses. Every key
 * MUST be present in every locale — `tests/i18n.test.ts` asserts
 * parity.
 */
export const REQUIRED_KEYS = [
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

export type RequiredKey = (typeof REQUIRED_KEYS)[number]

/** English resource — default, source of truth. */
export const EN = {
  chrome: {
    title: "  comuki · opentui-spike (core) · focus-mode",
    titleCompact: " comuki·opentui-spike",
  },
  composer: {
    placeholder: "Ask Comuki. Use / for actions or @ for knowledge.",
    placeholderCompact: "ask ›",
  },
  approval: {
    action: {
      approve: "approve",
      reject: "reject",
    },
    intentPrefix: "APPROVAL-INTENT:",
    scopePrefix: "APPROVAL-SCOPE:",
    riskPrefix: "APPROVAL-RISK:",
    planPrefix: "APPROVAL-PLAN:",
    stepPrefix: "APPROVAL-STEP:",
    diffPrefix: "APPROVAL-DIFF:",
    decideLabel: "  decide:",
  },
  cmd: {
    "open-palette": {
      label: "Open command palette",
      description: "Open the command palette (same registry as keystrokes).",
    },
    "close-palette": {
      label: "Close command palette",
      description: "Dismiss the palette overlay.",
    },
    "save-snippet": {
      label: "Save composer draft as snippet",
      description: "Persist the composer text into the snippet store.",
    },
    "approve-plan": {
      label: "Approve pending plan",
      description: "Send an approve decision for the awaiting-approval turn.",
    },
    "reject-plan": {
      label: "Reject pending plan",
      description: "Send a reject decision for the awaiting-approval turn.",
    },
    "queue-followup": {
      label: "Queue follow-up message",
      description: "Submit and queue a follow-up while the current turn runs.",
    },
    "submit-turn": {
      label: "Submit turn",
      description: "Plain Enter submits the composer.",
    },
    "copy-last-answer": {
      label: "Copy last assistant answer",
      description: "OSC 52 write of the most recent assistant text.",
    },
    "open-status": {
      label: "Open status panel",
      description: "Toggle the status overlay over the transcript viewport.",
    },
  },
} as const

/** Russian resource — parallel to EN, same key set. */
export const RU = {
  chrome: {
    title: "  comuki · opentui-spike (core) · focus-mode",
    titleCompact: " comuki·opentui-spike",
  },
  composer: {
    placeholder: "Спроси Comuki. / для действий, @ для знаний.",
    placeholderCompact: "спросить ›",
  },
  approval: {
    action: {
      approve: "одобрить",
      reject: "отклонить",
    },
    intentPrefix: "APPROVAL-INTENT:",
    scopePrefix: "APPROVAL-SCOPE:",
    riskPrefix: "APPROVAL-RISK:",
    planPrefix: "APPROVAL-PLAN:",
    stepPrefix: "APPROVAL-STEP:",
    diffPrefix: "APPROVAL-DIFF:",
    decideLabel: "  решай:",
  },
  cmd: {
    "open-palette": {
      label: "Открыть палитру команд",
      description: "Открыть палитру команд (тот же реестр, что и клавиатура).",
    },
    "close-palette": {
      label: "Закрыть палитру команд",
      description: "Скрыть оверлей палитры.",
    },
    "save-snippet": {
      label: "Сохранить черновик как сниппет",
      description: "Сохранить текст черновика в хранилище сниппетов.",
    },
    "approve-plan": {
      label: "Одобрить ожидающий план",
      description: "Отправить решение «одобрить» для ожидающего хода.",
    },
    "reject-plan": {
      label: "Отклонить ожидающий план",
      description: "Отправить решение «отклонить» для ожидающего хода.",
    },
    "queue-followup": {
      label: "Поставить follow-up в очередь",
      description: "Отправить и поставить follow-up в очередь во время текущего хода.",
    },
    "submit-turn": {
      label: "Отправить ход",
      description: "Обычный Enter отправляет черновик.",
    },
    "copy-last-answer": {
      label: "Скопировать последний ответ ассистента",
      description: "Запись в буфер обмена через OSC 52.",
    },
    "open-status": {
      label: "Открыть панель статуса",
      description: "Переключить оверлей статуса над transcript-окном.",
    },
  },
} as const

export const RESOURCES = { en: EN, ru: RU } as const

/**
 * Create a fully-initialized i18next instance for the given
 * locale. `init()` is awaited — by the time the returned promise
 * resolves, the instance's `language` and resource lookups are
 * ready for `tr`.
 */
export async function createI18nFor(locale: LocaleCode): Promise<I18nInstance> {
  const instance = i18next.createInstance()
  await instance.init({
    resources: {
      en: { spike: EN },
      ru: { spike: RU },
    },
    lng: locale,
    fallbackLng: DEFAULT_LOCALE,
    ns: [SPIKE_NAMESPACE],
    defaultNS: SPIKE_NAMESPACE,
    interpolation: { escapeValue: false },
    initImmediate: false,
  })
  return instance
}

/**
 * Return the localized value for `key` on the spike namespace.
 * Throws if the resolved value is empty or equal to the key
 * itself — that signals a missing key in the active locale and
 * the fallback, which is a build bug the spike's tests catch.
 */
export function tr(instance: I18nInstance, key: string): string {
  const value = instance.t(key, { ns: SPIKE_NAMESPACE })
  if (typeof value === "string" && value.length > 0 && value !== key) {
    return value
  }
  throw new Error(
    `i18n: missing or empty key '${key}' in locale '${instance.language}' (namespace '${SPIKE_NAMESPACE}')`,
  )
}
