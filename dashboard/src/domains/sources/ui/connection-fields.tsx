import type { ReactNode } from "react"

import { needsBaseUrl } from "@/domains/sources/model/providers"
import type { ProviderKey, SourceAuth } from "@/domains/sources/model/types"
import { TextField } from "@/shared/ui"

import { AuthKindChoice } from "@/domains/sources/ui/auth-kind-choice"

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
  kind: ProviderKey
  /**
   * The credential kind, already resolved through `effectiveAuth`.
   *
   * Resolved by the caller rather than here because the caller is what builds
   * the draft: a field that quietly showed one thing and a form that saved
   * another is the exact failure the derivation exists to prevent.
   */
  auth: SourceAuth
  baseUrl: string
  account: string
  /**
   * Overrides the registry's self-hostable answer.
   *
   * The edit form asks the row before it asks the registry — a connection
   * that already carries a base url keeps its box whatever the registry
   * knows about its provider, which is the difference between editing an
   * unknown provider's instance and losing it. The create form leaves it
   * unset and takes the registry's answer.
   */
  wantsHost?: boolean
  disabled?: boolean
  /**
   * A small control riding inside the base url's box, pinned to its end
   * edge — the probe button, so "the url and test it" reads as one control.
   *
   * Handed in rather than rendered here because the two forms probe
   * differently: the create form tests a draft it holds, the edit page
   * tests the stored connection, and the gating on each is the caller's
   * rule. Rendered only where there is a box to ride.
   */
  urlSuffix?: ReactNode
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
 * true on one screen and quietly false on the other — which is what had
 * happened: both forms had grown their own hardcoded credential lists beside
 * the registry's, and the edit form offered every credential to every
 * provider.
 *
 * The two rules, stated once here:
 *
 * - **The base url only exists for a kind that can be self-hosted.** A cloud
 *   GitHub has no instance to name, and a box asking for one is an invitation
 *   to type something that cannot be right. The edit form's `wantsHost`
 *   override is the row-before-registry exception, documented on the prop.
 * - **The credential row offers exactly what the chosen connector
 *   implements** — the registry's `auth` list and nothing else, through
 *   `AuthKindChoice`. There is no affordance here that could ask for a
 *   credential the dashboard cannot render, which is the point of a closed
 *   row. For a provider with no registry row the list is every credential the
 *   dashboard *can* render, which is an admission rather than a guess — see
 *   `providerAuth`.
 *
 * What is deliberately *not* here is the secret. It belongs to the one form
 * that takes it, it is said once above the box it is typed into, and a
 * connection that already exists has no field for it at all — see
 * `updateSeedConnection`.
 */
export function ConnectionFields({
  idPrefix,
  kind,
  auth,
  baseUrl,
  account,
  wantsHost,
  disabled = false,
  urlSuffix,
  onBaseUrlChange,
  onAuthChange,
  onAccountChange,
}: ConnectionFieldsProps) {
  const host = wantsHost ?? needsBaseUrl(kind)

  return (
    <>
      {host ? (
        <TextField
          id={`${idPrefix}-base-url`}
          label="base url"
          value={baseUrl}
          disabled={disabled}
          placeholder="https://git.example.internal"
          spellCheck={false}
          hint="self-hosted only. https, because the credential crosses this wire."
          suffix={urlSuffix}
          data-test={`${idPrefix}-base-url`}
          onValueChange={onBaseUrlChange}
        />
      ) : null}

      <AuthKindChoice
        name={`${idPrefix}-auth`}
        kind={kind}
        auth={auth}
        disabled={disabled}
        data-test={`${idPrefix}-auth`}
        onAuthChange={onAuthChange}
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
