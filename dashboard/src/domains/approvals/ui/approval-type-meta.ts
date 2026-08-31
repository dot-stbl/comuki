import type { ComponentType } from "react"
import { GitBranch, Image, Zap } from "lucide-react"

import type { ApprovalType } from "@/domains/approvals/model/types"

/**
 * The three kinds of decision, and the glyph each one wears.
 *
 * Its own file rather than a constant beside the badge that draws it: a module
 * that exports both a component and a value loses fast refresh, which is the
 * same reason `identity/model/tabs.ts` and `shared/ui/form/ids.ts` are theirs.
 *
 * `noun` is the word, and it is the *only* word — it names the chip and it
 * lands inside the accessible sentence on every decision button ("approve the
 * deploy for checkout-web"). There used to be a second, title-cased `label` for
 * the chip, which made one value read as two vocabularies; a value is spelled
 * the way it is stored, the way the kit's `StatusBadge` spells a status.
 */
export const APPROVAL_TYPE_META: Record<
  ApprovalType,
  { icon: ComponentType<{ className?: string }>; noun: string }
> = {
  plan: { icon: GitBranch, noun: "plan" },
  deploy: { icon: Zap, noun: "deploy" },
  baseline: { icon: Image, noun: "baseline" },
}
