import * as React from "react"

import { cn } from "@/shared/lib/utils"
import { toast } from "sonner"
import { Check, Copy } from "lucide-react"

function RunIdChip({ id, className }: { id: string; className?: string }) {
  const [copied, setCopied] = React.useState(false)

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(id)
      setCopied(true)
      toast.success("Copied", { description: id })
      setTimeout(() => setCopied(false), 2000)
    } catch {
      toast.error("Failed to copy", { description: id })
    }
  }

  return (
    <button
      type="button"
      onClick={handleCopy}
      className={cn(
        "inline-flex items-center gap-1 rounded bg-muted px-1.5 py-0.5 font-mono text-xs text-muted-foreground transition-colors hover:bg-muted/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        className
      )}
      title={id}
    >
      <span className="truncate max-w-[8rem]">{id}</span>
      {copied ? (
        <Check className="size-3 flex-shrink-0 text-st-success" />
      ) : (
        <Copy className="size-3 flex-shrink-0" />
      )}
    </button>
  )
}

export { RunIdChip }