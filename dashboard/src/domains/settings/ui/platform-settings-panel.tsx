import type { ReactNode } from "react"
import { useTranslation } from "react-i18next"

import type { PlatformSettings } from "@/domains/settings/model/types"
import { Section } from "@/shared/ui"

import styles from "./settings-panel.module.css"

/* The real-mode settings page: the host's read-only snapshot, drawn as the
 * registries this screen always was — hairline data surfaces, the figure as
 * the reading, the label naming it. There is no editor here on purpose: every
 * value is bound at boot, the host exposes no PUT, and a form over a
 * read-only surface is a phantom save waiting to be clicked. */

interface RowProps {
  label: string
  children: ReactNode
  /** What the figure is measuring, when the label alone does not say. */
  note?: string
}

function Row({ label, children, note }: RowProps) {
  return (
    <div className={styles.platformRow}>
      <span className={styles.platformLabel}>{label}</span>
      <span className={styles.platformValue}>{children}</span>
      {note ? <span className={styles.platformNote}>{note}</span> : null}
    </div>
  )
}

/** Seconds in the screen's tight duration spelling: `45s`, `5m`, `2h`. */
function seconds(seconds: number): string {
  if (seconds % 3600 === 0) {
    return `${seconds / 3600}h`
  }
  if (seconds % 60 === 0) {
    return `${seconds / 60}m`
  }
  return `${seconds}s`
}

export interface PlatformSettingsPanelProps {
  settings: PlatformSettings
}

export function PlatformSettingsPanel({
  settings,
}: PlatformSettingsPanelProps) {
  const { t } = useTranslation("settings")
  const lease = settings.orchestration.lease
  const escalation = settings.orchestration.escalationTimeout
  const scale = settings.compute.scale

  return (
    <div className={styles.platform}>
      <p className={styles.platformHint} data-test="settings-readonly-hint">
        {t("platform.hint")}
      </p>

      <Section
        variant="region"
        id="platform-orchestration"
        title={t("platform.orchestration.section")}
        data-test="settings-orchestration"
      >
        <div className={styles.platformRows}>
          <Row
            label={t("platform.orchestration.leaseTtl")}
            note={t("platform.orchestration.leaseTtlNote")}
          >
            {seconds(lease.leaseTtlSeconds)}
          </Row>
          <Row
            label={t("platform.orchestration.reapInterval")}
            note={t("platform.orchestration.reapIntervalNote")}
          >
            {seconds(lease.reapIntervalSeconds)}
          </Row>
          <Row
            label={t("platform.orchestration.reapGrace")}
            note={t("platform.orchestration.reapGraceNote")}
          >
            {seconds(lease.reapGraceSeconds)}
          </Row>
          <Row
            label={t("platform.orchestration.maxAttempts")}
            note={t("platform.orchestration.maxAttemptsNote")}
          >
            {lease.maxAttempts}
          </Row>
          <Row
            label={t("platform.orchestration.escalationTimeout")}
            note={t("platform.orchestration.escalationTimeoutNote")}
          >
            {escalation.enabled
              ? seconds(escalation.timeoutSeconds)
              : t("platform.escalationOff", {
                  would: seconds(escalation.timeoutSeconds),
                })}
          </Row>
          <Row
            label={t("platform.orchestration.escalationSweep")}
            note={t("platform.orchestration.escalationSweepNote")}
          >
            {seconds(escalation.sweepIntervalSeconds)}
          </Row>
        </div>
      </Section>

      <Section
        variant="region"
        id="platform-compute"
        title={t("platform.compute.section")}
        data-test="settings-compute"
      >
        <div className={styles.platformRows}>
          <Row
            label={t("platform.compute.provider")}
            note={t("platform.compute.providerNote")}
          >
            {settings.compute.provider}
          </Row>
          <Row
            label={t("platform.compute.workerImage")}
            note={t("platform.compute.workerImageNote")}
          >
            {scale.workerImage}
          </Row>
          <Row
            label={t("platform.compute.profilesRef")}
            note={t("platform.compute.profilesRefNote")}
          >
            {scale.profilesGitRef}
          </Row>
          <Row
            label={t("platform.compute.minIdle")}
            note={t("platform.compute.minIdleNote")}
          >
            {scale.minIdle}
          </Row>
          <Row
            label={t("platform.compute.maxConcurrent")}
            note={t("platform.compute.maxConcurrentNote")}
          >
            {scale.maxConcurrent}
          </Row>
          <Row
            label={t("platform.compute.idleTtl")}
            note={t("platform.compute.idleTtlNote")}
          >
            {seconds(scale.idleTtlSeconds)}
          </Row>
          <Row
            label={t("platform.compute.pollInterval")}
            note={t("platform.compute.pollIntervalNote")}
          >
            {seconds(scale.pollIntervalSeconds)}
          </Row>
        </div>
      </Section>

      <Section
        variant="region"
        id="platform-proxy"
        title={t("platform.proxy.section")}
        data-test="settings-proxy"
      >
        <div className={styles.platformRows}>
          <Row
            label={t("platform.proxy.passthrough")}
            note={t("platform.proxy.passthroughNote")}
          >
            {settings.proxy.enabled ? t("platform.on") : t("platform.off")}
          </Row>
        </div>
      </Section>
    </div>
  )
}
