import type { StorybookConfig } from "@storybook/react-vite"

const config: StorybookConfig = {
  framework: {
    name: "@storybook/react-vite",
    options: {},
  },
  stories: ["../src/**/*.stories.@(ts|tsx)"],
  addons: [
    "@storybook/addon-essentials",
    "@storybook/addon-themes",
    // TODO(phase-7): re-enable @storybook/addon-a11y when Storybook 10 ships (this project uses SB 8)
    // TODO(phase-7): re-enable @storybook/addon-vitest when Node.js 24 ESM compat is resolved
  ],
  docs: {},
}

export default config
