import {
  AUTH_BY_KIND,
  AUTH_LABEL,
  SOURCE_KIND_LABEL,
  needsBaseUrl,
} from "@/domains/sources/model/providers"
import type { SourceAuth, SourceKind } from "@/domains/sources/model/types"
import { SelectField, TextField } from "@/shared/ui"

export interface ConnectionFieldsProps {
  /**
   * What the three ids on this instance are prefixed with.
   *
   * There are two of these on the product now — one on `/sources/new` and one
   * in the connection region of a source's own page — and an `id` is a document
   * fact rather than a component fact, so the owner names them. It is also what
   * a test reaches for: the create page's boxes are `connect-*`, the edit
   * page's are `connection-*`, and no assertion has to say which form it meant.
   */
  idPrefix: string
  kind: SourceKind
  baseUrl: string
  account: string
  /**
   * The credential kind, already resolved through `effectiveAuth`.
   *
   * Resolved by the caller rather than here because the caller is what builds
   * the draft: a field that quietly showed one thing and a form that saved
   * another is the exact failure the derivation exists to prevent.
   */
  auth: SourceAuth
  disabled?: boolean
  onBaseUrlChange: (next: string) => void
  onAuthChange: (next: SourceAuth) => void
  onAccountChange: (next: string) => void
}

/**
 * Where the instance is and which credential reaches it — the three questions
 * that are the same on both sides of a connection's life.
 *
 * `/sources/new` asks them of a connection that does not exist yet;
 * `/sources/$sourceId` asks them of one that does. They were the same three
 * boxes with the same three rules, and writing them twice is how a rule ends up
 * true on one screen and quietly false on the other — the base url appearing
 * for a cloud provider, or an auth select offering a credential the connector
 * cannot use.
 *
 * The two rules, stated once here:
 *
 * - **The base url only exists for a kind that can be self-hosted.** A cloud
 *   GitHub has no instance to name, and a box asking for one is an invitation
 *   to type something that cannot be right.
 * - **The auth select offers exactly what the chosen connector implements** —
 *   `AUTH_BY_KIND` and nothing else. There is no affordance here that could ask
 *   for a seventh credential kind, which is the point of a closed list.
 *
 * What is deliberately *not* here is the secret. It belongs to the one form
 * that takes it, it is said once above the box it is typed into, and a
 * connection that already exists has no field for it at all — see
 * `updateSeedConnection`.
 */
export function ConnectionFields({
  idPrefix,
  kind,
  baseUrl,
  account,
  auth,
  disabled = false,
  onBaseUrlChange,
  onAuthChange,
  onAccountChange,
}: ConnectionFieldsProps) {
  const allowed = AUTH_BY_KIND[kind]

  return (
    <>
      {needsBaseUrl(kind) ? (
        <TextField
          id={`${idPrefix}-base-url`}
          label="base url"
          value={baseUrl}
          disabled={disabled}
          placeholder="https://git.example.internal"
          spellCheck={false}
          hint="self-hosted only. https, because the credential crosses this wire."
          data-test={`${idPrefix}-base-url`}
          onValueChange={onBaseUrlChange}
        />
      ) : null}

      <SelectField
        id={`${idPrefix}-auth`}
        label="auth"
        value={auth}
        disabled={disabled}
        options={allowed.map((entry) => ({
          value: entry,
          label: AUTH_LABEL[entry],
        }))}
        hint={`what ${SOURCE_KIND_LABEL[kind]} accepts, and nothing else.`}
        data-test={`${idPrefix}-auth`}
        onValueChange={(next: string) => onAuthChange(next as SourceAuth)}
      />

      <TextField
        id={`${idPrefix}-account`}
        label="account"
        value={account}
        disabled={disabled}
        placeholder="the bot or app the credential belongs to"
        spellCheck={false}
        hint="shown on the row afterwards, so a stale credential can be traced to a person."
        data-test={`${idPrefix}-account`}
        onValueChange={onAccountChange}
      />
    </>
  )
}
