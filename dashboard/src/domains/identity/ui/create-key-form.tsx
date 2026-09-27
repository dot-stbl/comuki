import { useEffect, useState } from "react"
import type { FormEvent } from "react"
import { useTranslation } from "react-i18next"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import { useIdentityQuery } from "@/domains/identity/api/queries"
import type { CreateApiKeyInput } from "@/domains/identity/model/types"
import { useCan } from "@/shared/session"
import { Button, Notice, SelectField, TextField } from "@/shared/ui"

/** The chosen lifetime as an ISO day, or `null` for a key that never expires. */
function expiryDay(days: string): string | null {
  const count = Number(days)
  if (!Number.isFinite(count) || count <= 0) {
    return null
  }
  return new Date(Date.now() + count * 86_400_000).toISOString().slice(0, 10)
}

export interface CreateKeyFormProps {
  busy?: boolean
  onCreate: (input: CreateApiKeyInput) => void
  onCancel: () => void
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Making a key: a name, an optional lifetime, an optional tenant scope,
 * and the warning that comes before all three.
 *
 * The rule this form is built around: **the plaintext is shown exactly once,
 * and the screen says so before it is generated, not after.** A person who did
 * not know cannot get it back — there is no second showing to recover it from,
 * because the store keeps only the prefix — so the warning is above the button
 * that creates the key rather than beside the value that has already appeared.
 * A rule explained after the fact is not an explanation; it is an apology.
 *
 * The tenant scope (Q11 / v1.1) lets the operator mint a key that only
 * authenticates requests carrying the matching <c>X-Comuki-Tenant</c>
 * header. The picker is empty by default; a non-empty choice is a key
 * scoped to one project, the wire shape that the BE accepts today.
 *
 * The showing itself is not here and is not on this page's URL. See
 * `key-secret-dialog.tsx` for why.
 */
export function CreateKeyForm({
  busy = false,
  onCreate,
  onCancel,
  onDirtyChange,
}: CreateKeyFormProps) {
  const { t } = useTranslation("identity")
  const manage = useCan("identity.manage")
  const identity = useIdentityQuery()

  const lifetimes = [
    { value: "0", label: t("keyForm.lifetimeNone") },
    { value: "30", label: t("keyForm.lifetime30") },
    { value: "90", label: t("keyForm.lifetime90") },
    { value: "365", label: t("keyForm.lifetime365") },
  ]

  const [name, setName] = useState("")
  const [lifetime, setLifetime] = useState("0")
  const [tenantProjectId, setTenantProjectId] = useState<string>("")

  // The tenant list is optional, so its wait is a hint rather than a gate —
  // but it is still a wait, and a picker that silently offers only "no tenant
  // scope" while the payload is in flight tells the operator this platform has
  // no projects.
  const scopesLoading = identity.isLoading
  const projectOptions = (identity.data?.projects ?? []).map((project) => ({
    value: project.id,
    label: `${project.name} (${project.slug})`,
  }))

  const tenantProjectSelected =
    tenantProjectId.length > 0 &&
    projectOptions.some((option) => option.value === tenantProjectId)

  const dirty = name !== "" || lifetime !== "0" || tenantProjectSelected

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (manage.denial || busy || !name.trim()) {
      return
    }
    onCreate({
      name: name.trim(),
      expiresAt: expiryDay(lifetime),
      tenantProjectId: tenantProjectSelected ? tenantProjectId : null,
    })
  }

  return (
    <FormLayout data-test="create-key" onSubmit={submit}>
      <FormFields>
        {/* Before the key exists, not after it has scrolled away. */}
        <Notice>{t("keyForm.notice")}</Notice>

        <TextField
          id="key-name"
          label={t("keyForm.nameLabel")}
          /* The submit is already gated on it — the marker only says so
             before the operator finds it out by being refused. */
          required
          autoFocus
          value={name}
          disabled={busy}
          spellCheck={false}
          autoComplete="off"
          placeholder={t("keyForm.namePlaceholder")}
          hint={t("keyForm.nameHint")}
          onValueChange={setName}
        />

        <SelectField
          id="key-lifetime"
          label={t("keyForm.expiresLabel")}
          value={lifetime}
          disabled={busy}
          options={lifetimes}
          hint={t("keyForm.expiresHint")}
          onValueChange={setLifetime}
        />

        <SelectField
          id="key-tenant"
          label={t("keyForm.tenantLabel")}
          value={tenantProjectId}
          disabled={busy || scopesLoading}
          options={[
            { value: "", label: t("keyForm.noTenantScope") },
            ...projectOptions,
          ]}
          hint={
            scopesLoading ? t("keyForm.tenantLooking") : t("keyForm.tenantHint")
          }
          onValueChange={setTenantProjectId}
        />
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="form-submit"
          denied={manage.denial}
          loading={busy}
          disabled={name.trim().length === 0}
        >
          {t("keyForm.submit")}
        </Button>
        <Button
          variant="secondary"
          data-test="form-cancel"
          disabled={busy}
          onClick={onCancel}
        >
          {t("actions.cancel", { ns: "common" })}
        </Button>
      </FormActions>
    </FormLayout>
  )
}
