import { Folder, GitBranch } from "lucide-react"

import {
  isImageIcon,
  resolveProjectMark,
} from "@/domains/projects/model/identity"
import { cn } from "@/shared/lib/utils"
import { BrandIcon } from "@/shared/ui"

import styles from "./project-mark.module.css"

/** The steps the mark glyph takes, by the same names the kit's icons use. */
export type ProjectMarkSize = "sm" | "md"

export interface ProjectMarkProps {
  /** The two fields the resolution reads — any `ProjectRow` satisfies this. */
  project: {
    icon: string | null
    gitProfileRepo: string | null
  }
  size?: ProjectMarkSize
}

/**
 * The mark a project answers to, drawn.
 *
 * Resolution is `resolveProjectMark`'s (design D3): a stored icon wins — an
 * emoji as text, a URL as an image — then a brand mark derived from the
 * profiles host, then the neutral glyph. Every branch of the result has a
 * drawing, so a row never renders a blank where its identity should be.
 *
 * The glyph is decorative wherever it sits: the surfaces that show it (the
 * registry's identity cell, the detail page's facts) spell the project out in
 * words beside it — the slug, the name, the repository URL — so the mark
 * reinforces a stated fact rather than carrying one alone. That is the same
 * two-channel rule a status band follows, with the words as the second
 * channel.
 */
export function ProjectMark({ project, size = "sm" }: ProjectMarkProps) {
  const mark = resolveProjectMark(project)
  const className = cn(styles.glyph, styles[size])

  if (mark.kind === "stored") {
    return isImageIcon(mark.value) ? (
      <img src={mark.value} alt="" aria-hidden="true" className={className} />
    ) : (
      <span aria-hidden="true" className={cn(className, styles.emoji)}>
        {mark.value}
      </span>
    )
  }

  if (mark.kind === "brand") {
    if (mark.brand === "github" || mark.brand === "gitlab") {
      // The kit's own marks, drained to the chrome — `label={null}` because
      // the row states the repository in full beside this (the profiles
      // column, the facts list), so the mark is reinforcement, not the
      // only place the provider is named.
      return <BrandIcon brand={mark.brand} size={size} label={null} />
    }
    return <GitBranch aria-hidden="true" className={className} />
  }

  // No repository to derive from: a plain folder, not a git-marked one — the
  // glyph says "a project", not "a repo somewhere unnamed".
  return <Folder aria-hidden="true" className={className} />
}
