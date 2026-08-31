import { useMemo } from "react"

import { PROJECT_BY_APP } from "@/shared/api/mock/runs.seed"
import { useSession } from "@/shared/session"

import type { SearchCatalogue } from "./shapes"

/**
 * The two closed lists the catalogue tier of the resolver matches against.
 *
 * **Projects** come off the session, which already carries every project this
 * shift can see — the same list every toolbar builds its project filter from.
 *
 * **Applications** are a platform fact the backend will one day serve beside
 * `GET /resolve?q=`; until then the mapping in the run seed *is* the platform's
 * register of applications, and it is read here rather than copied, so the day
 * a twelfth application is seeded the palette knows about it without an edit.
 *
 * Both are catalogues, not data: the palette matches a query against a dozen
 * names, never against a list of rows. That distinction is the whole reason
 * this is not a client-side search — see the note at the top of `shapes.ts`.
 */
const PLATFORM_APPS = Object.keys(PROJECT_BY_APP).sort()

export function useSearchCatalogue(): SearchCatalogue {
  const session = useSession()

  return useMemo(
    () => ({ projects: session.projects, apps: PLATFORM_APPS }),
    [session.projects]
  )
}
