import type { ComponentType } from "react"
import { KeyRound, Link, Package } from "lucide-react"

import {
  AUTH_LABEL,
  providerAuth,
  providerLabel,
} from "@/domains/sources/model/providers"
import type { ProviderKey, SourceAuth } from "@/domains/sources/model/types"
import { cn } from "@/shared/lib/utils"

import styles from "./auth-kind-choice.module.css"

/* The muted silhouette each credential wears. An icon per kind, because the
   three credentials are three different objects an operator has held — a
   token somebody pasted once, a grant a browser walked through, an
   installation a marketplace owns — and the glyph is the recognition cue
   the words then confirm. Muted, never accent: the selection is the
   segment's own reading, and an icon shouting in brand colour would be a
   second selection channel the border did not agree with. */
const AUTH_ICONS: Partial<Record<SourceAuth, ComponentType<GlyphProps>>> = {
  pat: KeyRound,
  oauth: Link,
  "app-install": Package,
}

interface GlyphProps {
  className?: string
  "aria-hidden"?: boolean
}

export interface AuthKindChoiceProps {
  /** Names the group for assistive tech, and draws it. */
  label?: string
  /** The radios' one `name` — the arrow-key group, the single tab stop. */
  name: string
  kind: ProviderKey
  /**
   * The credential the form is holding, already resolved through
   * `effectiveAuth` by the caller — the one that builds the draft.
   */
  auth: SourceAuth
  disabled?: boolean
  "data-test"?: string
  onAuthChange: (next: SourceAuth) => void
}

/**
 * Which credential reaches the provider, asked as a row of segments.
 *
 * **Exactly what the chosen connector implements, and nothing else** — the
 * registry's `auth` list per provider, so yandex tracker never offers a
 * credential its connector cannot use and github never offers an oauth
 * grant it does not speak. There is no affordance here that could ask for a
 * credential the dashboard cannot render, which is the point of a closed
 * row; for a provider with no registry row the list is every credential the
 * dashboard *can* render, which is an admission rather than a guess — see
 * `providerAuth`.
 *
 * The construction is the compact card row the task priority field and the
 * intake's source cards share: a real `<input type="radio">` off-screen
 * under each segment, so the arrow-key group, the single tab stop and the
 * announced role are the platform's rather than reimplemented. Segments
 * rather than the kit `ChoiceField`'s stacked boxes because this stands
 * directly beneath a row of provider *cards* — a second stack of
 * full-width boxes with sentences would make the form all stacks — and the
 * credential words ("personal access token") already say what admission
 * modes need their sentences for. The provider-specific rule travels on the
 * hint instead.
 *
 * A domain component for the same reason the registry is: the kit has no
 * opinion about this product's credential kinds.
 */
export function AuthKindChoice({
  label = "auth kind",
  name,
  kind,
  auth,
  disabled = false,
  "data-test": dataTest,
  onAuthChange,
}: AuthKindChoiceProps) {
  const allowed = providerAuth(kind)

  return (
    <fieldset
      className={styles.fieldset}
      aria-label={label}
      data-test={dataTest}
    >
      <span className={styles.label}>{label}</span>
      <div className={styles.segments}>
        {allowed.map((candidate) => {
          const selected = auth === candidate
          const Icon = AUTH_ICONS[candidate]
          return (
            <label
              key={candidate}
              className={cn(styles.segment, selected && styles.selected)}
              data-test={`${name}-option`}
              data-value={candidate}
              data-selected={selected || undefined}
            >
              <input
                type="radio"
                name={name}
                className={styles.input}
                value={candidate}
                checked={selected}
                disabled={disabled}
                aria-label={AUTH_LABEL[candidate]}
                onChange={() => onAuthChange(candidate)}
              />
              {Icon ? (
                <Icon className={styles.icon} aria-hidden={true} />
              ) : null}
              <span className={styles.segmentLabel}>
                {AUTH_LABEL[candidate]}
              </span>
            </label>
          )
        })}
      </div>
      {/* The sentence the closed list used to carry, still said once: these
          are not every credential there is, they are the ones *this*
          connector implements, and the provider's own word is what makes
          that a fact rather than a coincidence. */}
      <span className={styles.hint}>
        what {providerLabel(kind)} accepts, and nothing else. Stored verbatim
        in the settings json; never holds a credential.
      </span>
    </fieldset>
  )
}
