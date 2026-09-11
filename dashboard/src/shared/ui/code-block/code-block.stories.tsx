import type { Meta, StoryObj } from "@storybook/react"

import { CodeBlock } from "./code-block"

/* Every state the owner screenshots, and one for each decision the component
   makes: which grammar, how long before it folds, what a line too wide for the
   box does, and what a language nobody registered looks like. */

const meta: Meta<typeof CodeBlock> = {
  title: "UI Kit/Data/CodeBlock",
  component: CodeBlock,
  tags: ["autodocs"],
  parameters: { layout: "padded" },
  argTypes: {
    language: { control: "text" },
    path: { control: "text" },
    startLine: { control: "number" },
    collapseAfter: { control: "number" },
    wrap: { control: "boolean" },
  },
  decorators: [
    (Story) => (
      <div style={{ maxInlineSize: "44rem" }}>
        <Story />
      </div>
    ),
  ],
}

export default meta
type Story = StoryObj<typeof CodeBlock>

const TS = `import { runs } from "@/shared/api"

/** Everything still moving, newest first. */
export async function listRunning(projectId: string) {
  const page = await runs.list({ projectId, status: ["running", "waiting"] })
  return page.items.sort((a, b) => b.startedAt.localeCompare(a.startedAt))
}
`

/** The default reading: a language the highlighter knows, short enough to stand open. */
export const Default: Story = {
  args: { source: TS, language: "ts" },
}

/** With an origin. `path:line` is two values, so the data voice and tabular figures. */
export const WithOrigin: Story = {
  args: {
    source: TS,
    language: "typescript",
    path: "src/domains/runs/api/queries.ts",
    startLine: 128,
  },
}

/** C#, because half of this product is. */
export const CSharp: Story = {
  args: {
    language: "csharp",
    path: "Comuki.Modules.Chat/ChatMessage.cs",
    startLine: 41,
    source: `public sealed record ChatMessage(
    Guid Id,
    ChatMessageRole Role,
    /// <summary>Markdown. Always has been.</summary>
    string Content,
    DateTimeOffset CreatedAt)
{
    public bool IsToolObservation => Role == ChatMessageRole.Tool;
}
`,
  },
}

/**
 * A patch. The hue is not carrying this — the `+` and `-` are in the source
 * and the wash only finds the lines they sit on, so the reading survives
 * greyscale and colour blindness the way every other two-channel cue here does.
 */
export const Diff: Story = {
  args: {
    language: "diff",
    path: "src/domains/chat/ui/chat-thread.tsx",
    source: `@@ -64,9 +64,14 @@ export function ChatThread({ messages }: ChatThreadProps) {
   useEffect(() => {
     const port = scroll.current
-    if (port) {
-      port.scrollTop = port.scrollHeight
-    }
+    if (!port || !pinned.current) {
+      return
+    }
+    port.scrollTop = port.scrollHeight
   }, [messages])
`,
  },
}

/** YAML, where the keys and the values are the whole reading. */
export const Yaml: Story = {
  args: {
    language: "yml",
    source: `profiles:
  implementer:
    model: claude-sonnet-4
    budget: 2.50      # dollars per work item
    tools: [repo.read, repo.write, tests.run]
  reviewer:
    model: claude-opus-4
    budget: 0.80
`,
  },
}

/**
 * A language nobody registered. The chip says `text` rather than claiming a
 * grammar, and the code is on screen exactly as it arrived — an unstyled block
 * that says why it is unstyled beats a block that looks broken.
 */
export const UnknownLanguage: Story = {
  args: {
    language: "brainfuck",
    source: `++++++++[>++++[>++>+++>+++>+<<<<-]>+>+>->>+[<]<-]>>.>
---.+++++++..+++.>>.<-.<.+++.------.--------.>>+.>++.
`,
  },
}

/**
 * Past the fold. Twenty-four lines is the default, and the control says how
 * many are behind it — a clipped box with no count is a box nobody expands.
 */
export const Collapsed: Story = {
  args: {
    language: "ts",
    path: "src/shared/api/mock/runs.seed.ts",
    startLine: 1,
    source: Array.from(
      { length: 62 },
      (_, index) =>
        `  { id: "r_${(index + 1).toString(16).padStart(4, "0")}", stage: "w${(index % 9) + 1}", status: "running" },`
    ).join("\n"),
  },
}

/**
 * A line with no break opportunity in it. Off by default the block scrolls
 * sideways — the Shape-Not-Reading Rule — and one press turns wrapping on.
 */
export const WithLongText: Story = {
  args: {
    language: "bash",
    source: `# one line, no break opportunities, exactly the case that breaks a layout
curl -sS -X POST https://orchestrator.comuki.internal/api/v1/runs -H 'content-type: application/json' -H 'authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1X29wZXJhdG9yIiwicHJvamVjdCI6InBfY29tdWtpIn0' -d '{"projectId":"p_comuki","ticket":"COM-4181","profile":"implementer"}'
`,
  },
}

/** The same block with wrapping already on, which is what the toggle produces. */
export const Wrapped: Story = {
  args: { ...WithLongText.args, wrap: true } as Story["args"],
}

/** One line. No fold, and the controls still sit where they always sit. */
export const SingleLine: Story = {
  args: { language: "bash", source: "bun run test -- --reporter=verbose" },
}
