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
    "@storybook/addon-a11y",
    // @storybook/addon-vitest is SB 10+ only; on SB 8 we run stories as
    // component tests via @storybook/test-runner (npm run test:storybook).
  ],
  docs: {},
}

export default config
