/**
 * Public surface of the <c>work</c> dashboard domain. Mirrors the
 * <c>domains/inbox</c> barrel shape: callers import <c>@/domains/work</c>
 * and reach <c>queries</c> / <c>types</c> through the barrel. The
 * detail screen <c>WorkTaskDetailPage</c> is the first page-side export.
 */

export { WorkTaskDetailPage } from "@/domains/work/pages/work-task-detail-page"
export type { WorkTaskDetailPageProps } from "@/domains/work/pages/work-task-detail-page"

export {
  workTaskQueryKey,
  useWorkTaskQuery,
} from "@/domains/work/api/queries"

export type {
  WorkTaskResolutionOutcome,
  WorkTaskSourceRefView,
  WorkTaskStatus,
  WorkTaskSummary,
} from "@/domains/work/model/types"
