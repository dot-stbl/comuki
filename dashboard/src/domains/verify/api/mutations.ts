import { useMutation, useQueryClient } from "@tanstack/react-query"

import { verifyQueryKey } from "@/domains/verify/api/queries"
import type { VerifySnapshot } from "@/domains/verify/model/types"
import { setSeedVerifyEnabled } from "@/shared/api/mock/verify.store"
import { env } from "@/shared/config/env"

/**
 * The one act this screen offers.
 *
 * There is deliberately no mutation for a command. A command is a line in the
 * client's git, so the only thing that changes one is a commit in their
 * repository — the screen's job is to say where that repository is, not to
 * offer an editor that would have to be disabled.
 */

export interface SetVerifyEnabledInput {
  projectId: string
  enabled: boolean
}

export function useSetVerifyEnabled() {
  const client = useQueryClient()

  return useMutation<unknown, Error, SetVerifyEnabledInput>({
    mutationFn: async ({ projectId, enabled }) => {
      if (!env.useMock) {
        throw new Error("verify settings not implemented — set VITE_USE_MOCK=true")
      }
      await new Promise((resolve) => setTimeout(resolve, 220))
      setSeedVerifyEnabled(projectId, enabled)
      return { projectId, enabled }
    },
    onMutate: async ({ projectId, enabled }) => {
      await client.cancelQueries({ queryKey: verifyQueryKey })
      const previous = client.getQueryData<VerifySnapshot>(verifyQueryKey)

      client.setQueryData<VerifySnapshot>(verifyQueryKey, (snapshot) =>
        snapshot
          ? {
              ...snapshot,
              projects: snapshot.projects.map((project) =>
                project.projectId === projectId
                  ? { ...project, enabled }
                  : project
              ),
            }
          : snapshot
      )

      return { previous }
    },
    onError: (_error, _input, context) => {
      const previous = (context as { previous?: VerifySnapshot } | undefined)
        ?.previous
      if (previous) {
        client.setQueryData(verifyQueryKey, previous)
      }
    },
    onSettled: async () => {
      await client.invalidateQueries({ queryKey: verifyQueryKey })
    },
  })
}
