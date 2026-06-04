import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { createRouter, RouterProvider } from "@tanstack/react-router"

import { ThemeProvider } from "@/components/theme-provider"

import { routeTree } from "./routeTree.gen"

import "./index.css"

const router = createRouter({
  routeTree,
  defaultPreload: "intent",
  defaultPreloadStaleTime: 0,
})

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router
  }
}

const rootElement = document.getElementById("root")!
if (!rootElement.innerHTML) {
  createRoot(rootElement).render(
    <StrictMode>
      <ThemeProvider defaultTheme="dark" storageKey="comuki-ui-theme">
        <RouterProvider router={router} />
      </ThemeProvider>
    </StrictMode>,
  )
}
