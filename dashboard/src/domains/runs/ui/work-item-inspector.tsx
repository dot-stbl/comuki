import type { ComponentType, ReactNode } from "react"
import {
  Boxes,
  BookOpen,
  CornerDownRight,
  FileCode2,
  FileText,
  FlaskConical,
  Lock,
  Server,
  Terminal,
} from "lucide-react"

import type {
  WorkItem,
  WorkItemInspector as WorkItemInspectorModel,
} from "@/domains/runs/model/types"
import {
  isLongEdge,
  type ItemDependency,
} from "@/domains/runs/model/work-items"
import { cn } from "@/shared/lib/utils"
import { StatusBadge } from "@/shared/ui"

import styles from "./work-item-inspector.module.css"

/**
 * The work item inspector — the graph's companion.
 *
 * The graph says where an item sits and what state it is in; this says what it
 * actually is. What it shows about a step comes from the step's **profile**,
 * which is declared in git and knowable; the heading is the brain's own name
 * for it, which is prose and keys nothing.
 *
 * It is also where a long dependency stops being a hint and becomes something
 * you can act on: everything the item waits on is listed with its distance,
 * and each entry moves the graph's cursor.
 */

export interface WorkItemInspectorProps {
  item: WorkItem
  /** Position in dependency order — "item 4 of 8". */
  index: number
  total: number
  info: WorkItemInspectorModel
  /** What this item waits on, nearest column first. */
  waitsOn?: ItemDependency[]
  /** Moving the graph's cursor onto a dependency. */
  onSelect?: (itemId: string) => void
  className?: string
}

/**
 * The profile catalog's icon vocabulary. Unknown names fall back rather than
 * throwing: the catalog lives in a client's git and can grow a word this build
 * has never seen.
 */
const ICONS: Record<string, ComponentType<{ className?: string }>> = {
  book: BookOpen,
  box: Boxes,
  file: FileText,
  flask: FlaskConical,
  lock: Lock,
  server: Server,
  terminal: Terminal,
}

function iconFor(name: string): ComponentType<{ className?: string }> {
  return ICONS[name] ?? Boxes
}

/** How far back a dependency sits, in the reader's terms rather than in maths. */
function distanceOf(dependency: ItemDependency): string {
  return dependency.span <= 1
    ? "previous column"
    : `${dependency.span} columns back`
}

interface SectionProps {
  title: string
  /** A quiet word at the right of the rule — a count, a property, a caveat. */
  note?: string
  children: ReactNode
}

function Section({ title, note, children }: SectionProps) {
  return (
    <section className={styles.section}>
      <h3 className={styles.sectionHead}>
        {title}
        {note ? <span className={styles.sectionNote}>{note}</span> : null}
      </h3>
      {children}
    </section>
  )
}

interface FigureProps {
  name: string
  value: string
}

function Figure({ name, value }: FigureProps) {
  return (
    <div className={styles.figure}>
      <dt className={styles.figureName}>{name}</dt>
      <dd className={styles.figureValue}>{value}</dd>
    </div>
  )
}

interface WaitsOnProps {
  waitsOn: ItemDependency[]
  onSelect?: (itemId: string) => void
}

/**
 * Everything the item waits on, nearest first, with the distance written out.
 *
 * This is the other half of how the long edge is solved. The graph marks the
 * node and lights the connection on hover or focus; a hover is no answer at
 * all for a screen reader and a poor one for a keyboard, so the same set is
 * here as ordinary controls that move the graph's cursor. Near dependencies
 * are listed too — a person should not have to learn that "no distance shown"
 * means "the column beside it".
 */
function WaitsOn({ waitsOn, onSelect }: WaitsOnProps) {
  if (waitsOn.length === 0) {
    return <p className={styles.none}>Nothing — this item starts the plan.</p>
  }

  return (
    <ul className={styles.deps}>
      {waitsOn.map((dependency) => {
        const distance = distanceOf(dependency)
        const long = isLongEdge(dependency)
        const body = (
          <>
            <CornerDownRight className={styles.depIcon} aria-hidden="true" />
            <span className={styles.depLabel}>{dependency.item.label}</span>
            <span className={styles.depProfile}>{dependency.item.profile}</span>
            <span className={styles.depSpan}>{distance}</span>
          </>
        )

        if (!onSelect) {
          return (
            <li
              key={dependency.item.id}
              className={cn(styles.dep, long && styles.depLong)}
              data-test="work-item-dependency"
              data-item={dependency.item.id}
            >
              {body}
            </li>
          )
        }

        return (
          <li key={dependency.item.id}>
            <button
              type="button"
              className={cn(styles.dep, long && styles.depLong)}
              data-test="work-item-dependency"
              data-item={dependency.item.id}
              aria-label={`Show ${dependency.item.label}, ${dependency.item.profile}, ${dependency.item.status}, ${distance}.`}
              onClick={() => onSelect(dependency.item.id)}
            >
              {body}
            </button>
          </li>
        )
      })}
    </ul>
  )
}

export function WorkItemInspectorPanel({
  item,
  index,
  total,
  info,
  waitsOn = [],
  onSelect,
  className,
}: WorkItemInspectorProps) {
  const longCount = waitsOn.filter(isLongEdge).length

  return (
    <div
      className={cn(styles.inspector, className)}
      data-test="work-item-inspector"
    >
      <header className={styles.head}>
        <div className={styles.identity}>
          {/* The brain's prose. The graph clamps it to two lines; here it is
              written out, because this is the only place it fully lives. */}
          <h2 className={styles.title}>{item.label}</h2>
          <p className={styles.ident}>
            <span className={styles.identStrong}>{item.profile}</span>
            <span aria-hidden="true">·</span>
            <span>{info.role}</span>
            <span aria-hidden="true">·</span>
            <span>
              item {index} of {total}
            </span>
          </p>
        </div>
        <StatusBadge status={item.status} className={styles.badge} />
      </header>

      <dl className={styles.figures}>
        <Figure name="env" value={info.env} />
        <Figure name="tokens" value={info.tokens} />
        <Figure name="cost" value={`$${info.cost}`} />
        <Figure name="started" value={item.startedAt ?? "—"} />
      </dl>

      <div className={styles.panes}>
        <div className={styles.pane}>
          <Section
            title="waits on"
            note={
              longCount > 0
                ? `${longCount} more than one column back`
                : undefined
            }
          >
            <WaitsOn waitsOn={waitsOn} onSelect={onSelect} />
          </Section>

          <Section title="input — what fed it">
            <ul className={styles.chips}>
              {info.inputs.map((entry) => {
                const Icon = iconFor(entry.icon)
                return (
                  <li
                    key={`${entry.label}-${entry.detail ?? ""}`}
                    className={styles.chip}
                  >
                    <Icon className={styles.chipIcon} aria-hidden="true" />
                    {entry.label}
                    {entry.detail ? (
                      <span className={styles.chipDetail}>{entry.detail}</span>
                    ) : null}
                  </li>
                )
              })}
            </ul>
          </Section>

          <Section title="log" note="append-only">
            <ol className={styles.log}>
              {info.events.map((event, eventIndex) => (
                <li
                  key={`${event.time}-${eventIndex}`}
                  className={styles.event}
                  data-status={event.status}
                >
                  <span className={styles.eventTime}>{event.time}</span>
                  <span className={styles.eventText}>{event.text}</span>
                </li>
              ))}
            </ol>
          </Section>
        </div>

        <div className={styles.pane}>
          {info.gate ? (
            <Section title="verification gate">
              <ul className={styles.chips}>
                {info.gate.map((check) => (
                  <li key={check.name}>
                    <StatusBadge status={check.status} size="sm">
                      {check.name}
                    </StatusBadge>
                  </li>
                ))}
              </ul>
            </Section>
          ) : null}

          <Section title="output — what it produced">
            {info.files ? (
              <div className={styles.files}>
                {info.files.map((file) => (
                  <div key={file.path} className={styles.file}>
                    <div className={styles.fileHead}>
                      <FileCode2
                        className={styles.chipIcon}
                        aria-hidden="true"
                      />
                      <span className={styles.filePath}>{file.path}</span>
                      <span className={styles.fileAdded}>+{file.added}</span>
                      <span className={styles.fileDeleted}>
                        −{file.deleted}
                      </span>
                    </div>
                    <pre className={styles.hunk}>
                      {file.lines.map((line, lineIndex) => (
                        <div
                          key={`${file.path}-${lineIndex}`}
                          className={styles.line}
                          data-kind={line.kind}
                        >
                          <span className={styles.lineNumber}>{line.line}</span>
                          {/* The sign, not the tint, is what carries the
                              reading when colour is gone. */}
                          <span className={styles.lineSign}>
                            {line.kind === "add"
                              ? "+"
                              : line.kind === "del"
                                ? "−"
                                : " "}
                          </span>
                          <span>{line.text}</span>
                        </div>
                      ))}
                    </pre>
                  </div>
                ))}
              </div>
            ) : info.outputs.length > 0 ? (
              <ul className={styles.outputs}>
                {info.outputs.map((entry) => {
                  const Icon = iconFor(entry.icon)
                  return (
                    <li
                      key={`${entry.label}-${entry.detail ?? ""}`}
                      className={styles.output}
                    >
                      <Icon className={styles.chipIcon} aria-hidden="true" />
                      {entry.label}
                      {entry.detail ? (
                        <span className={styles.outputDetail}>
                          {entry.detail}
                        </span>
                      ) : null}
                    </li>
                  )
                })}
              </ul>
            ) : (
              <p className={styles.none}>Nothing yet.</p>
            )}
          </Section>
        </div>
      </div>
    </div>
  )
}
