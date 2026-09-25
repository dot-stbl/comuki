import { Check, GitBranch, Lock } from "lucide-react"

import { useEdition, useFeature } from "@/shared/editions/queries"
import { FeatureGate, Section } from "@/shared/ui"

import styles from "./edition-panel.module.css"

/**
 * The edition snapshot for the read-only settings page in real mode.
 *
 * Every value out of a closed vocabulary (`tier` / `status` /
 * capability keys / limit keys / semver), each row a one-line "what
 * we know" statement: no toggles, no buttons, no upgrade CTA. The
 * capability matrix is ALWAYS rendered — a Community reader should
 * see every paid capability and its lock mark, so the page can answer
 * "what would change under a paid license" without forcing a second
 * call to the catalog. The locked affordance is reserved for the one
 * piece of the panel where it is the honest reading: a paid-only
 * action row (`multi-repo` — "connect repository"). This panel is
 * read-only by design, so the action row is presentational and
 * disabled, not a live button.
 */
export function EditionPanel() {
  const query = useEdition()
  const multiRepo = useFeature(query.data, "multi-repo")

  if (query.isPending) {
    return (
      <Section
        variant="region"
        id="platform-edition"
        title="edition"
        data-test="edition-loading"
      >
        <p className={styles.hint}>loading edition snapshot…</p>
      </Section>
    )
  }

  if (query.isError) {
    return (
      <Section
        variant="region"
        id="platform-edition"
        title="edition"
        data-test="edition-error"
      >
        <p className={styles.hint}>edition snapshot did not load.</p>
      </Section>
    )
  }

  const snapshot = query.data
  const projects = snapshot.limits.find((limit) => limit.key === "projects")

  return (
    <Section
      variant="region"
      id="platform-edition"
      title="edition"
      data-test="settings-edition"
    >
      <div className={styles.row}>
        <span className={styles.label}>tier</span>
        <span className={styles.value} data-test="edition-tier">
          {snapshot.tier}
        </span>
      </div>
      <div className={styles.row}>
        <span className={styles.label}>status</span>
        <span className={styles.value} data-test="edition-status">
          {snapshot.status}
        </span>
      </div>
      <div className={styles.row}>
        <span className={styles.label}>version</span>
        <span className={styles.value} data-test="edition-version">
          {snapshot.version}
        </span>
      </div>
      {snapshot.expiresAt ? (
        <div className={styles.row}>
          <span className={styles.label}>expires</span>
          <span className={styles.value} data-test="edition-expires">
            {snapshot.expiresAt}
          </span>
        </div>
      ) : null}
      {projects ? (
        <div className={styles.row}>
          <span className={styles.label}>projects</span>
          <span className={styles.value} data-test="edition-projects">
            {projects.current} / {projects.cap}
          </span>
        </div>
      ) : null}

      <ul className={styles.features} data-test="edition-features">
        {snapshot.features.map((feature) => (
          <li key={feature.key} className={styles.feature}>
            <span className={styles.featureKey}>{feature.key}</span>
            <span
              className={styles.featureMark}
              data-available={feature.available ? "yes" : "no"}
              aria-label={
                feature.available
                  ? `${feature.key} is available`
                  : `${feature.key} is not in this edition`
              }
            >
              {feature.available ? (
                <Check aria-hidden="true" />
              ) : (
                <Lock aria-hidden="true" />
              )}
            </span>
          </li>
        ))}
      </ul>

      <FeatureGate feature="multi-repo" available={multiRepo}>
        <div
          className={styles.row}
          data-test="edition-multirepo-affordance"
          aria-disabled="true"
        >
          <span className={styles.label}>
            <GitBranch aria-hidden="true" className={styles.labelIcon} />
            multi-repo
          </span>
          <span className={styles.value}>connect repository</span>
        </div>
      </FeatureGate>
    </Section>
  )
}
