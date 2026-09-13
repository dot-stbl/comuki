import type { ReactNode } from "react"

import type {
  PlatformSettings,
} from "@/domains/settings/model/types"
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

export function PlatformSettingsPanel({ settings }: PlatformSettingsPanelProps) {
  const lease = settings.orchestration.lease
  const escalation = settings.orchestration.escalationTimeout
  const scale = settings.compute.scale

  return (
    <div className={styles.platform}>
      <p className={styles.platformHint} data-test="settings-readonly-hint">
        Read-only — every value here is configured at boot. Changes go through
        configuration and a restart; nothing on this page writes.
      </p>

      <Section
        variant="region"
        id="platform-orchestration"
        title="orchestration"
        data-test="settings-orchestration"
      >
        <div className={styles.platformRows}>
          <Row label="lease ttl" note="handed out on claim, extended by heartbeat">
            {seconds(lease.leaseTtlSeconds)}
          </Row>
          <Row label="reap interval" note="lease reaper sweep cadence">
            {seconds(lease.reapIntervalSeconds)}
          </Row>
          <Row label="reap grace" note="buffer past expiry before the reaper acts">
            {seconds(lease.reapGraceSeconds)}
          </Row>
          <Row label="max attempts" note="claims before a stalled item fails">
            {lease.maxAttempts}
          </Row>
          <Row
            label="escalation timeout"
            note="how long a run may sit escalated before auto-archival"
          >
            {escalation.enabled
              ? seconds(escalation.timeoutSeconds)
              : `off — the ratchet is disabled (${seconds(escalation.timeoutSeconds)} would apply)`}
          </Row>
          <Row label="escalation sweep" note="ratchet sweep cadence">
            {seconds(escalation.sweepIntervalSeconds)}
          </Row>
        </div>
      </Section>

      <Section
        variant="region"
        id="platform-compute"
        title="compute"
        data-test="settings-compute"
      >
        <div className={styles.platformRows}>
          <Row label="provider" note="the compute implementation v1 runs">
            {settings.compute.provider}
          </Row>
          <Row label="worker image" note="digest-pinned in production">
            {scale.workerImage}
          </Row>
          <Row label="profiles ref" note="the pinned profiles git ref">
            {scale.profilesGitRef}
          </Row>
          <Row label="min idle" note="warm-idle floor per profile">
            {scale.minIdle}
          </Row>
          <Row label="max concurrent" note="concurrency cap per project">
            {scale.maxConcurrent}
          </Row>
          <Row label="idle ttl" note="before a worker is a reaper candidate">
            {seconds(scale.idleTtlSeconds)}
          </Row>
          <Row label="poll interval" note="between supervisor passes">
            {seconds(scale.pollIntervalSeconds)}
          </Row>
        </div>
      </Section>

      <Section
        variant="region"
        id="platform-proxy"
        title="proxy"
        data-test="settings-proxy"
      >
        <div className={styles.platformRows}>
          <Row
            label="openai / anthropic passthrough"
            note="whether the proxy is composed at all"
          >
            {settings.proxy.enabled ? "on" : "off"}
          </Row>
        </div>
      </Section>
    </div>
  )
}
