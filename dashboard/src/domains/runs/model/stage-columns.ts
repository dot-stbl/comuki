import type { RunStage } from "@/domains/runs/model/types"

export interface StageColumn {
  parallel: boolean
  stages: RunStage[]
}

export function stageColumns(stages: RunStage[]): StageColumn[] {
  const columns: StageColumn[] = []
  let index = 0

  while (index < stages.length) {
    const stage = stages[index]
    if (stage.lane) {
      const group: RunStage[] = []
      while (index < stages.length && stages[index].lane) {
        group.push(stages[index])
        index += 1
      }
      columns.push({ parallel: true, stages: group })
      continue
    }
    columns.push({ parallel: false, stages: [stage] })
    index += 1
  }

  return columns
}
