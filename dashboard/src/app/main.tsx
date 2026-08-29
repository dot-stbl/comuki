import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { createRouter, RouterProvider } from "@tanstack/react-router"

import { AppProviders } from "@/app/providers"
import { env } from "@/shared/config/env"

import { routeTree } from "../routeTree.gen"

import "../index.css"

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

async function bootstrap() {
  if (env.useMock) {
    const { startMocks } = await import("@/app/mocks/start")
    await startMocks()
  }

  const rootElement = document.getElementById("root")!
  if (!rootElement.innerHTML) {
    createRoot(rootElement).render(
      <StrictMode>
        <AppProviders>
          <RouterProvider router={router} />
        </AppProviders>
      </StrictMode>,
    )
  }
}

void bootstrap()
