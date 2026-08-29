export function formatDuration(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds))
  const minutes = Math.floor(safe / 60)
  const rem = safe % 60
  return `${String(minutes).padStart(2, "0")}:${String(rem).padStart(2, "0")}`
}

export function formatCost(value: number): string {
  return `$${value.toFixed(2)}`
}

export function formatTokens(tokens: number): string {
  if (tokens <= 0) {
    return "0"
  }
  return `${(tokens / 1000).toFixed(1)}k`
}
