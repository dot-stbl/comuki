import { useState } from "react"
import { useTranslation } from "react-i18next"

import type { ProjectRow } from "@/domains/projects/model/types"
import { ColorField } from "@/domains/projects/ui/color-field"
import { TagEntryField } from "@/domains/projects/ui/tag-entry-field"
import { Button, TextField } from "@/shared/ui"

import styles from "./project-identity-editor.module.css"

/** What the editor writes — the identity slice of a `ProjectUpdate`. */
export interface ProjectIdentityPatch {
  icon: string | null
  color: string | null
  tags: string[]
}

export interface ProjectIdentityEditorProps {
  project: ProjectRow
  busy?: boolean
  onSave: (patch: ProjectIdentityPatch) => void
  onCancel: () => void
}

/**
 * The inline editor for a project's identity — three optional fields, not a
 * page (design D8).
 *
 * The PATCH contract shapes two of the hints. `null` means *untouched* on the
 * wire, so an emptied icon field keeps the stored icon rather than clearing
 * it (clearing is deliberately not part of this surface), and the colour's
 * refusal says "keep stored" rather than "none" for the same reason. Tags are
 * the one field where the editor can honestly say "remove": an empty list is
 * a real PATCH value that clears the stored vocabulary.
 *
 * The panel starts from the stored values and saves exactly what it shows;
 * an unchanged save is a no-op on the server, and a cancelled one never
 * leaves the screen.
 */
export function ProjectIdentityEditor({
  project,
  busy = false,
  onSave,
  onCancel,
}: ProjectIdentityEditorProps) {
  const { t } = useTranslation("projects")
  const [icon, setIcon] = useState(project.icon ?? "")
  const [color, setColor] = useState<string | null>(project.color)
  // The row's tags are readonly (a record is read, not edited in place); the
  // editor works on its own copy and hands the copy to the PATCH.
  const [tags, setTags] = useState<string[]>([...project.tags])

  return (
    <div className={styles.editor} data-test="identity-editor">
      <TextField
        id="identity-icon"
        label={t("identity.icon")}
        value={icon}
        disabled={busy}
        spellCheck={false}
        autoComplete="off"
        placeholder={t("identity.iconPlaceholder")}
        hint={t("editor.iconHint")}
        onValueChange={setIcon}
      />

      <ColorField
        id="identity-color"
        label={t("identity.accentColour")}
        value={color}
        onValueChange={setColor}
        clearLabel={t("editor.colorClear")}
        disabled={busy}
        hint={t("editor.colorHint")}
      />

      <TagEntryField
        id="identity-tags"
        value={tags}
        onValueChange={setTags}
        disabled={busy}
        hint={t("editor.tagsHint")}
      />

      <div className={styles.actions}>
        <Button
          size="sm"
          data-test="identity-editor-save"
          loading={busy}
          onClick={() => {
            onSave({
              icon: icon.trim() || null,
              color,
              tags,
            })
          }}
        >
          {t("editor.save")}
        </Button>
        <Button
          variant="secondary"
          size="sm"
          data-test="identity-editor-cancel"
          disabled={busy}
          onClick={onCancel}
        >
          {t("actions.cancel", { ns: "common" })}
        </Button>
      </div>
    </div>
  )
}
