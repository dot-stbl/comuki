import axe, { type Result as AxeResult } from "axe-core"
import type { Preview } from "@storybook/react-vite"

import "../src/index.css"

/* The real translation instance (`en` active): stories render the same
   catalogues the app does, so a component drawn in a story shows its
   words rather than key paths. */
import "../src/shared/i18n"

const preview: Preview = {
  parameters: {
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/,
      },
    },
    backgrounds: {
      default: "dark",
      values: [
        { name: "dark", value: "#15171B" },
        { name: "light", value: "#FBFBFA" },
      ],
    },
    // `@storybook/addon-a11y` is installed (its axe-core + the test-utils
    // `afterEach` shape that addon-vitest uses) but its own per-story
    // failure pass is off — the `afterEach` below is the one that
    // consults `a11y-known-issues.json` and decides "known → warning,
    // unknown → fail". addon-a11y's stock model has no allowlist hook, so
    // its `test: 'error'` mode would fail the run on every pre-existing
    // violation the JSON documents. Disabling it here and re-running the
    // same axe-core rules under our own `afterEach` keeps the wire shape
    // (vitest's `expect` matcher semantics, one-shot per story-test) and
    // the allowlist semantics at the same time.
    a11y: { test: "off" },
  },
  globalTypes: {
    theme: {
      description: "Global theme for components",
      defaultValue: "dark",
      toolbar: {
        title: "Theme",
        icon: "circlehollow",
        items: [
          { value: "dark", icon: "moon", title: "Dark" },
          { value: "light", icon: "sun", title: "Light" },
        ],
        dynamicTitle: true,
      },
    },
  },
  decorators: [
    (Story, context) => {
      const theme = context.globals.theme ?? "dark"
      document.documentElement.classList.toggle("dark", theme === "dark")
      return Story()
    },
  ],
  // Storybook 10's preview.addons surface — `afterEach` runs after the
  // story's render and any `play` function complete, on the same
  // channel as addon-a11y's own pass (which we disabled above). Throwing
  // here fails the surrounding vitest story-test, exactly like
  // `expect(result).toHaveNoViolations()` would for addon-a11y's stock
  // path. Sync reads of the post-render DOM are unsafe; this is async.
  async afterEach({ id }) {
    const storyId = id ?? "unknown"
    const themes: readonly string[] = ["dark", "light"]
    let hadUnknown = false
    let lastMessage = ""
    for (const theme of themes) {
      document.documentElement.classList.toggle("dark", theme === "dark")
      // Theme tokens resolve via `.dark` class toggle on `<html>`; the
      // toggle before `axe.run` is what makes both passes land on the same
      // post-render DOM the story's own render produced for each theme.
      const result: AxeResult = await axe.run(document.body, {
        runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] },
      })
      const unknown = result.violations.filter(
        (violation) => !KNOWN_A11Y_ISSUES.has(`${storyId}|${theme}|${violation.id}`),
      )
      const knownCount = result.violations.length - unknown.length
      if (knownCount > 0) {
        console.warn(
          `[test:stories] ${storyId} (${theme}): ${knownCount} known a11y violation(s) allowlisted via storybook-tests/a11y-known-issues.json — not failing`,
        )
      }
      if (unknown.length > 0) {
        const lines = unknown.map((violation) => {
          const targets = violation.nodes
            .map((node) => node.target.join(" "))
            .join(", ")
          return `${violation.id} (${violation.impact ?? "unknown"}): ${violation.help} — ${targets}`
        })
        lastMessage = `[a11y:${theme}] ${storyId} — ${lines.join("\n")}`
        hadUnknown = true
      }
    }
    if (hadUnknown) {
      throw new Error(lastMessage)
    }
  },
}

export default preview