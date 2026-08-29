import path from "path"
import { tanstackRouter } from "@tanstack/router-plugin/vite"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    // tanstackRouter MUST come before react — generates routeTree.gen.ts
    tanstackRouter({ target: "react" }),
    react(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    // Comuki port pool 17000–17200 — see .agents/rules/process/ports.md
    port: 17173,
    strictPort: true,
  },
})
