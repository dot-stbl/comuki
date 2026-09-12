import { StrictMode } from "react"
import { createRoot } from "react-dom/client"
import { RouterProvider } from "@tanstack/react-router"

import { AppProviders } from "@/app/providers"
import { router } from "@/app/router"
import { env } from "@/shared/config/env"

import "../index.css"

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
