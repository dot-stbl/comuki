import { useMemo, useRef, useState, type KeyboardEvent } from "react"
import { SendHorizontal, X } from "lucide-react"

import type { SearchTarget } from "@/app/search"
import {
  commandMenuQuery,
  commandOf,
  matchCommands,
  scopeState,
} from "@/domains/chat/model/commands"
import type { SlashCommand } from "@/domains/chat/model/types"
import { useSession } from "@/shared/session"
import { Button, Select, Tooltip } from "@/shared/ui"

import styles from "./chat-composer.module.css"

/** The box starts here, so a three-line thought does not need a scrollbar. */
const MIN_ROWS = 3
/** And stops here, so the thread does not disappear behind a pasted file. */
const MAX_ROWS = 12

export interface ChatComposerProps {
  /** Every command this session may be offered — built-in plus the client's. */
  commands: SlashCommand[]
  onSend: (text: string, projectId?: string) => void
  busy?: boolean
  /**
   * The half-typed message, held by the container. The composer is mounted in
   * the route and in the dock's sheet; the sheet is closed and reopened while
   * somebody is mid-sentence, and a draft is a thought rather than a property
   * of whichever box is showing it.
   */
  value: string
  onValueChange: (next: string) => void
  /**
   * What the operator was looking at when the console was opened, offered as
   * context rather than imposed: the chip leaves in one gesture, and the id
   * rides along only with the message it was attached to.
   */
  seed?: SearchTarget | null
  onSeedChange?: (next: SearchTarget | null) => void
}

/**
 * Where the operator types, and the two controls that live with the typing.
 *
 * ## The box
 *
 * Enter sends and shift-enter breaks a line, which is the convention every
 * console has and the one people type without thinking. The box stands three
 * lines and grows to twelve: a long paste has to *look* long, because the
 * commonest defect in a composer is a pasted stack trace silently becoming one
 * line of a five-line box and getting sent half-read. `rows` grows with the
 * newlines — which is what jsdom can see and a test can assert — and
 * `field-sizing: content` in the stylesheet handles the wrapped-but-unbroken
 * case in a browser that has it.
 *
 * ## The menu
 *
 * Open while the *first* token is a slash word, closed the moment a space is
 * typed: a menu over an argument is a menu in the way. Built-in commands and
 * the client's own are one list, distinguished by a mark rather than by being
 * in two places — the whole point of a custom command is that it behaves like
 * a built-in one.
 *
 * It is a list of real buttons rather than a listbox with a roving
 * `aria-activedescendant`. A textarea cannot be a combobox without giving up
 * being multiline, and every option here is a control with a real name — so
 * arrow-down walks into the list, escape walks back, and nothing had to be
 * re-implemented.
 *
 * ## The scope chip — an adaptation, not the requirement as written
 *
 * §7 asked for a project chip, and meant a mode. There is no current project in
 * this product any more, so the chip is an explicit scope on the commands that
 * genuinely cannot proceed without one — and it says which command it is
 * scoping, rather than sitting there permanently implying the conversation is
 * "in" somewhere. `model/commands.ts` holds the rule and the argument for it.
 */
export function ChatComposer({
  commands,
  onSend,
  busy,
  value,
  onValueChange,
  seed,
  onSeedChange,
}: ChatComposerProps) {
  const session = useSession()
  const box = useRef<HTMLTextAreaElement | null>(null)
  const menu = useRef<HTMLUListElement | null>(null)

  const [projectId, setProjectId] = useState("")
  const [dismissed, setDismissed] = useState(false)

  const command = useMemo(
    () => commandOf(value, commands),
    [value, commands]
  )
  const menuQuery = commandMenuQuery(value)
  const matches = useMemo(
    () => (menuQuery === null ? [] : matchCommands(menuQuery, commands)),
    [menuQuery, commands]
  )
  const menuOpen = !dismissed && matches.length > 0

  const scope = scopeState(session, command, projectId)
  const rows = Math.min(
    MAX_ROWS,
    Math.max(MIN_ROWS, value.split("\n").length)
  )

  const empty = value.trim().length === 0
  const sendDenied = scope.denied
  const sendDisabled = Boolean(busy) || empty || Boolean(scope.incomplete)

  const complete = (chosen: SlashCommand) => {
    onValueChange(`${chosen.name} `)
    setDismissed(true)
    box.current?.focus()
  }

  const send = () => {
    if (sendDisabled || sendDenied) {
      return
    }
    // The seeded reference rides along as text, spelled in parentheses, for
    // three reasons that are one: the tokenizer links identifiers wherever
    // they appear, so the id becomes the thing it names in the sent message;
    // the script reads its subject out of the message, so a command aimed at
    // a seeded run is not asked a second time; and the thread reads honestly
    // — what was asked, and about what. Already spelled by hand, not repeated.
    const typed = value.trim()
    const withSeed =
      seed && !typed.includes(seed.id) ? `${typed} (${seed.id})` : typed
    onSend(withSeed, scope.needed ? projectId : undefined)
    onValueChange("")
    onSeedChange?.(null)
    setDismissed(false)
  }

  const onBoxKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === "Escape" && menuOpen) {
      event.preventDefault()
      setDismissed(true)
      return
    }
    if (event.key === "ArrowDown" && menuOpen) {
      event.preventDefault()
      menu.current?.querySelector<HTMLButtonElement>("button")?.focus()
      return
    }
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault()
      // Completes a command that is still being typed, and sends one that is
      // already whole: `/hel` + enter is a completion, `/help` + enter is the
      // command. Without the second half, typing a command exactly and
      // pressing enter would silently do nothing but add a space.
      if (menuOpen && matches[0] && matches[0].name !== menuQuery) {
        complete(matches[0])
        return
      }
      send()
    }
  }

  const onOptionKeyDown = (
    event: KeyboardEvent<HTMLButtonElement>,
    index: number
  ) => {
    if (event.key === "Escape") {
      event.preventDefault()
      setDismissed(true)
      box.current?.focus()
      return
    }
    if (event.key !== "ArrowDown" && event.key !== "ArrowUp") {
      return
    }
    event.preventDefault()
    const options = Array.from(
      menu.current?.querySelectorAll<HTMLButtonElement>("button") ?? []
    )
    const step = event.key === "ArrowDown" ? 1 : -1
    const next = index + step
    if (next < 0) {
      box.current?.focus()
      return
    }
    options[Math.min(next, options.length - 1)]?.focus()
  }

  return (
    <div className={styles.composer} data-test="chat-composer">
      {menuOpen ? (
        <div className={styles.menuWrap}>
          <p className={styles.menuHead}>commands</p>
          <ul className={styles.menu} ref={menu} data-test="chat-slash-menu">
            {matches.map((entry, index) => (
              <li key={entry.name}>
                <button
                  type="button"
                  className={styles.option}
                  data-test="chat-slash-option"
                  data-origin={entry.origin}
                  onKeyDown={(event) => onOptionKeyDown(event, index)}
                  onClick={() => complete(entry)}
                >
                  <span className={styles.optionName}>{entry.name}</span>
                  <span className={styles.optionDescription}>
                    {entry.description}
                  </span>
                  {/* A client command says whose it is. It is not a lesser
                      command — it just did not come from the platform, and the
                      operator has to know which git to go and read. */}
                  {entry.origin === "client" ? (
                    <span className={styles.optionOrigin}>
                      from {entry.projectId?.replace(/^p_/, "")}
                    </span>
                  ) : null}
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <div className={styles.box}>
        <textarea
          ref={box}
          className={styles.input}
          data-test="chat-input"
          rows={rows}
          value={value}
          aria-label="Message the console"
          placeholder="Ask, or start with a slash"
          onKeyDown={onBoxKeyDown}
          onChange={(event) => {
            onValueChange(event.target.value)
            setDismissed(false)
          }}
        />

        <div className={styles.controls}>
          {/* The seeded reference: the console was opened *from* somewhere, and
              this is the somewhere, carried in past the scrim. The chip is its
              own remove control — one chip, one target, one gesture — because a
              suggestion the operator must notice-and-delete is a decision they
              did not make. The name says which reference it drops: × is not the
              name of anything. */}
          {seed ? (
            <button
              type="button"
              className={styles.seed}
              data-test="chat-seed"
              data-kind={seed.kind}
              aria-label={`Drop the reference to ${seed.id}`}
              onClick={() => onSeedChange?.(null)}
            >
              <span className={styles.seedAbout}>about</span>
              <span className={styles.seedId}>{seed.id}</span>
              <X className={styles.seedX} aria-hidden="true" />
            </button>
          ) : null}

          {scope.needed && scope.command ? (
            <span className={styles.scope} data-test="chat-scope">
              <span className={styles.scopeLabel}>
                <span className={styles.scopeCommand}>
                  {scope.command.name}
                </span>{" "}
                runs in
              </span>
              {scope.choices.length > 0 ? (
                <Select
                  size="sm"
                  value={projectId}
                  onValueChange={setProjectId}
                  options={scope.choices.map((project) => ({
                    value: project.id,
                    label: project.key,
                  }))}
                  placeholder="pick a project"
                  aria-label={`Project ${scope.command.name} runs in`}
                  data-test="chat-scope-select"
                />
              ) : (
                <span className={styles.scopeNone}>{scope.denied}</span>
              )}
            </span>
          ) : null}

          <span className={styles.spacer} />

          {scope.incomplete ? (
            <span className={styles.incomplete} data-test="chat-incomplete">
              {scope.incomplete}
            </span>
          ) : null}

          <Tooltip content="Send — enter">
            <Button
              size="icon-sm"
              data-test="chat-send"
              aria-label="Send"
              disabled={sendDisabled}
              denied={sendDenied}
              onClick={send}
            >
              <SendHorizontal aria-hidden="true" />
            </Button>
          </Tooltip>
        </div>
      </div>
    </div>
  )
}
