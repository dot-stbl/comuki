import type { PlanEdge, PlanNode } from "@/domains/chat/model/types"

import styles from "./chat-message.module.css"

export interface PlanSketchProps {
  nodes: PlanNode[]
  edges: PlanEdge[]
}

/**
 * A plan the turn laid out — **a stub for P2, said in words**.
 *
 * The drawing exists already: the run graph on `/runs/$runId` lays a plan out
 * in depth bands, and it is the thing an operator approves against. Drawing a
 * second picture of the same graph here, before the two shapes are the same
 * shape, is how two pictures of one plan start disagreeing — and this console
 * has exactly one rule about second drawings of things that have a screen.
 *
 * So until the graph primitive is shared, this is the reading without the
 * picture: the nodes in the order the turn gave them, each with the profile
 * that would run it, and every dependency said out loud on the node that
 * waits for it. Nothing here is invented and nothing is lost — an operator can
 * check the plan, which is the only thing the drawing was for.
 */
export function PlanSketch({ nodes, edges }: PlanSketchProps) {
  const labels = new Map(nodes.map((node) => [node.id, node.label]))

  return (
    <ol className={styles.plan} data-test="chat-plan">
      {nodes.map((node) => {
        const waitsFor = edges
          .filter((edge) => edge.to === node.id)
          .map((edge) => labels.get(edge.from) ?? edge.from)

        return (
          <li className={styles.planNode} key={node.id} data-node={node.id}>
            <span className={styles.planProfile}>{node.profile ?? "—"}</span>
            <span className={styles.planLabel}>
              {node.label}
              {waitsFor.length > 0 ? (
                <span className={styles.planAfter}>
                  {" "}
                  after {waitsFor.join(", ")}
                </span>
              ) : null}
            </span>
          </li>
        )
      })}
    </ol>
  )
}
