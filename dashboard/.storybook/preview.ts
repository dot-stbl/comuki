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
}

export default preview
