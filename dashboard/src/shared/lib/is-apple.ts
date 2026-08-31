const APPLE = /mac|iphone|ipad|ipod/i

/**
 * Whether the keyboard in front of the operator says command or control.
 *
 * A shortcut spelled on screen must be the one the operator can actually
 * press, and the honest spelling differs by platform: `⌘ j` on an Apple
 * keyboard, `ctrl j` everywhere else. Promoted from `global-search.tsx` when
 * the console dock grew a chord of its own — two consumers, one spelling.
 */
export function isApple(): boolean {
  return (
    typeof navigator !== "undefined" && APPLE.test(navigator.userAgent ?? "")
  )
}
