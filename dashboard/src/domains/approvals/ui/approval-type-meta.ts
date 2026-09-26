import type { ComponentType } from "react"
import { Flag, GitBranch, Image, Lightbulb, Zap } from "lucide-react"

import type { ApprovalType } from "@/domains/approvals/model/types"

/**
 * The kinds of decision, and the glyph each one wears.
 *
 * Its own file rather than a constant beside the badge that draws it: a module
 * that exports both a component and a value loses fast refresh, which is the
 * same reason `identity/model/tabs.ts` and `shared/ui/form/ids.ts` are theirs.
 *
 * The noun is not here — it lives in the `approvals` catalogue
 * (`approvals:type.<kind>`) and is read where it renders, because a constant
 * cannot answer in the active locale. There used to be a second, title-cased
 * `label` for the chip, which made one value read as two vocabularies; a value
 * is spelled the way it is stored, the way the kit's `StatusBadge` spells a
 * status.
 *
 * `gate` is the wire's kind: a run the orchestrator escalated back to a human.
 * It is not a plan or a deploy — the host does not say *why* it escalated, and
 * the chip that named a reason it does not have would be the first lie on a
 * screen whose whole job is a decision.
 *
 * `learning` is a rule a worker proposed through learning.suggest. What is
 * being decided is the rule, so that is the noun.
 */
export const APPROVAL_TYPE_META: Record<
  ApprovalType,
  { icon: ComponentType<{ className?: string }> }
> = {
  plan: { icon: GitBranch },
  deploy: { icon: Zap },
  baseline: { icon: Image },
  gate: { icon: Flag },
  learning: { icon: Lightbulb },
}
