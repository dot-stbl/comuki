import { useMutation, useQueryClient } from "@tanstack/react-query"

import { env } from "@/shared/config/env"
import { PROCEDURES_PROPOSED_DIFF_SEED } from "@/shared/api/mock"
import { postApiV1ProceduresProjectidProcedurekeyProposePatch } from "@/shared/api/_generated/clients/postApiV1ProceduresProjectidProcedurekeyProposePatch"
import { postApiV1ProceduresProjectidProcedurekeyPublish } from "@/shared/api/_generated/clients/postApiV1ProceduresProjectidProcedurekeyPublish"
import type { PostApiV1ProceduresProjectidProcedurekeyProposePatchMutationRequest } from "@/shared/api/_generated/types/PostApiV1ProceduresProjectidProcedurekeyProposePatch"
import type { PostApiV1ProceduresProjectidProcedurekeyProposePatchMutationResponse } from "@/shared/api/_generated/types/PostApiV1ProceduresProjectidProcedurekeyProposePatch"
import type { PostApiV1ProceduresProjectidProcedurekeyPublishMutationRequest } from "@/shared/api/_generated/types/PostApiV1ProceduresProjectidProcedurekeyPublish"
import type { PostApiV1ProceduresProjectidProcedurekeyPublishMutationResponse } from "@/shared/api/_generated/types/PostApiV1ProceduresProjectidProcedurekeyPublish"

import { mapProposedPatchResponse } from "@/domains/procedures/api/mappers"
import type { ProposedProcedurePatch } from "@/domains/procedures/model/types"
import {
  procedureVersionQueryKey,
  procedureCompiledVersionQueryKey,
} from "@/domains/procedures/api/queries"

/**
 * Brain-facing propose-patch mutation — the chat calls this when an
 * operator asks for a procedure change. The wire's response is a
 * `ProposedPatchResponse` carrying the full semantic diff; we project
 * to the domain `ProposedProcedurePatch` so the Studio canvas reads
 * one shape end-to-end.
 *
 * On success, the mutation invalidates both query keys that depend on
 * the procedure: the latest-version key (the pin moves when a new
 * version is published) and the by-id key (a Studio canvas opened on
 * a specific version id needs to re-fetch when the new version is
 * compiled).
 */
async function postProposePatch(
  projectId: string,
  procedureKey: string,
  request: PostApiV1ProceduresProjectidProcedurekeyProposePatchMutationRequest,
): Promise<ProposedProcedurePatch> {
  if (env.useMock) {
    return {
      patchId: `mock-${Date.now()}`,
      baseVersionId: request.baseVersionId,
      rationale: request.rationale,
      diff: PROCEDURES_PROPOSED_DIFF_SEED,
    }
  }
  const wire = (await postApiV1ProceduresProjectidProcedurekeyProposePatch(
    projectId,
    procedureKey,
    request,
  )) as PostApiV1ProceduresProjectidProcedurekeyProposePatchMutationResponse
  return mapProposedPatchResponse(wire)
}

export function useProposePatchMutation(projectId: string, procedureKey: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (
      request: PostApiV1ProceduresProjectidProcedurekeyProposePatchMutationRequest,
    ) => postProposePatch(projectId, procedureKey, request),
    onSettled: () => {
      void client.invalidateQueries({
        queryKey: procedureVersionQueryKey(projectId, procedureKey),
      })
    },
  })
}

/**
 * Human-publish mutation — the human clicks "publish" in Studio after
 * reviewing the rendered diff. The wire body is the
 * `PublicationRequestDto` (patch + layered procedure + policy context
 * + approver); the page assembles it from the latest version + a
 * layered-procedure echo + the workbench's policy context.
 *
 * The in-process crown e2e (PHASE 3) calls
 * `IPublicationService.PublishAsync` directly via DI; this hook is the
 * Studio-driven path the same way.
 */
export function usePublishProcedureMutation(projectId: string, procedureKey: string) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (
      request: PostApiV1ProceduresProjectidProcedurekeyPublishMutationRequest,
    ) =>
      postApiV1ProceduresProjectidProcedurekeyPublish(
        projectId,
        procedureKey,
        request,
      ).then((result) => {
        const body = result as PostApiV1ProceduresProjectidProcedurekeyPublishMutationResponse
        return body
      }),
    onSettled: () => {
      void client.invalidateQueries({
        queryKey: procedureVersionQueryKey(projectId, procedureKey),
      })
      void client.invalidateQueries({
        queryKey: procedureCompiledVersionQueryKey(""),
      })
    },
  })
}