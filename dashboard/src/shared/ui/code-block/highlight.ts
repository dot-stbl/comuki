import type { HLJSApi, LanguageFn } from "highlight.js"

/**
 * Syntax highlighting, and why it is highlight.js rather than shiki.
 *
 * Shiki tokenises against a TextMate grammar and a VS Code theme, and it emits
 * the theme **inlined into the markup** — a `style="color:#..."` per span. This
 * product ships seven themes in two readings each, `DESIGN.md` forbids a
 * hardcoded colour in a component, and a colour baked into a span is a colour
 * that cannot follow a palette it was not compiled against. highlight.js emits
 * *classes* (`hljs-keyword`, `hljs-string`), which `code-block.module.css`
 * styles with `var(--token)` — so the highlighting follows every theme,
 * including the ones that do not exist yet.
 *
 * ## Nine names, eight grammars, all of them lazy
 *
 * Registration is explicit and on demand: the core is ~30kB and each grammar
 * is a few more, and neither has any business in the first paint of a screen
 * that may never show a line of code. Nothing is imported until a block on
 * screen asks for it, and each grammar is imported exactly once per session.
 *
 * The list is closed. `tsx` and the two JavaScript spellings ride the
 * TypeScript grammar — a superset, and not a tenth grammar to load — and a
 * language outside the list renders as plain text rather than reaching for the
 * "common" bundle, which is every grammar highlight.js has.
 */

/** The registered names, in the spelling the chip shows. */
export const CODE_LANGUAGES = [
  "bash",
  "cs",
  "diff",
  "json",
  "markdown",
  "sql",
  "ts",
  "tsx",
  "yaml",
] as const

export type CodeLanguage = (typeof CODE_LANGUAGES)[number]

/**
 * The spellings a turn actually writes, mapped onto the nine.
 *
 * A model writes ` ```csharp `, an operator pastes ` ```sh `, and a linter
 * writes ` ```yml `. None of them is a new grammar; all of them are the same
 * grammar under the name whoever typed the fence happened to know.
 */
const ALIASES: Readonly<Record<string, CodeLanguage>> = {
  bash: "bash",
  sh: "bash",
  shell: "bash",
  zsh: "bash",
  console: "bash",
  cs: "cs",
  "c#": "cs",
  csharp: "cs",
  dotnet: "cs",
  diff: "diff",
  patch: "diff",
  json: "json",
  jsonc: "json",
  json5: "json",
  markdown: "markdown",
  md: "markdown",
  mdx: "markdown",
  sql: "sql",
  postgres: "sql",
  postgresql: "sql",
  ts: "ts",
  typescript: "ts",
  js: "ts",
  javascript: "ts",
  mjs: "ts",
  tsx: "tsx",
  jsx: "tsx",
  yaml: "yaml",
  yml: "yaml",
}

/**
 * The grammar loaders. One dynamic import each, so the bundler gives every
 * grammar its own chunk and a thread full of YAML never pays for C#.
 */
const LOADERS: Readonly<
  Record<CodeLanguage, () => Promise<{ default: LanguageFn }>>
> = {
  bash: () => import("highlight.js/lib/languages/bash"),
  cs: () => import("highlight.js/lib/languages/csharp"),
  diff: () => import("highlight.js/lib/languages/diff"),
  json: () => import("highlight.js/lib/languages/json"),
  markdown: () => import("highlight.js/lib/languages/markdown"),
  sql: () => import("highlight.js/lib/languages/sql"),
  ts: () => import("highlight.js/lib/languages/typescript"),
  // Not a tenth grammar: highlight.js has no separate TSX and the TypeScript
  // one reads JSX well enough to be worth the same chunk twice over.
  tsx: () => import("highlight.js/lib/languages/typescript"),
  yaml: () => import("highlight.js/lib/languages/yaml"),
}

/** The one the chip shows when the fence named nothing this build knows. */
export const PLAIN_LANGUAGE_LABEL = "text"

/**
 * The grammar a fence's language string resolves to, or `null` for plain text.
 *
 * Case and whitespace are normalised because a fence is hand-typed, and a
 * `language-` prefix is stripped because that is how markdown hands it over.
 */
export function resolveLanguage(spelling?: string): CodeLanguage | null {
  if (!spelling) {
    return null
  }
  const cleaned = spelling.trim().toLowerCase().replace(/^language-/, "")
  return ALIASES[cleaned] ?? null
}

let core: Promise<HLJSApi> | null = null
const registered = new Set<CodeLanguage>()

function loadCore(): Promise<HLJSApi> {
  core ??= import("highlight.js/lib/core").then((module) => module.default)
  return core
}

/**
 * The source, as highlighted markup.
 *
 * The returned string is safe to inject and this is the only reason the
 * component is allowed to: highlight.js escapes `&`, `<` and `>` out of the
 * source before it wraps anything, so the only markup in the result is the
 * `<span class="hljs-…">` it emitted itself. Nothing the operator or the model
 * wrote can become an element — which is the same guarantee the markdown
 * renderer buys by refusing `rehype-raw`.
 *
 * `ignoreIllegals` because a code block in a conversation is frequently a
 * *fragment*, and a grammar that gives up on an unbalanced brace would leave
 * the reader staring at unstyled text for no reason they could see.
 */
export async function highlightCode(
  source: string,
  language: CodeLanguage
): Promise<string> {
  const hljs = await loadCore()
  if (!registered.has(language)) {
    const grammar = await LOADERS[language]()
    hljs.registerLanguage(language, grammar.default)
    registered.add(language)
  }
  return hljs.highlight(source, { language, ignoreIllegals: true }).value
}
