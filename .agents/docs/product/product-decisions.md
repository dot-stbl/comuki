# Product Decisions — ответы на 43 вопроса audit-product-report.md

> **Owner:** bradw (acting as product owner for v1) — 2026-09-08
>
> **Source:** [`../audits/audit-product-report.md`](../audits/audit-product-report.md) (§5 Product Questions for the Owner)
>
> **Convention:** каждое решение краткое, с конкретным next-step. Где решение требует кода, отмечено как `[FIX]` (для немедленного фикса) или `[V2]` (deferred).

---

## UX / feature (Q1–Q15)

**Q1. Run detail page в real-mode — half-shell.**
**Decision:** Добавить endpoint `GET /api/v1/runs/{id}` который возвращает полный `RunDetail` (workItems, brief, events summary, plan). FE mapper уже готов, нужно наполнить wire. **`[FIX]` — критично, 1 day.**

**Q2. Bulk cancel на runs list.**
**Decision:** v2. Backend endpoint `POST /api/v1/runs/bulk-cancel` — overkill для v1. Оператор может cancel по одному через существующий `POST /cancel`.

**Q3. Cost page — mock-first несмотря на BE.**
**Decision:** Wire к `GET /api/v1/projects/{id}/costs`. Platform-wide aggregation — v2 (новый endpoint `GET /api/v1/costs`). **`[FIX]` — высокий приоритет, 0.5 day.**

**Q4. Queue page — ever wired?**
**Decision:** v2. Runs view (queued/running/waiting) уже покрывает 90% use case. Queue page остаётся mock-first.

**Q5. Multi-IdP button.**
**Decision:** v1.1 — добавить `GET /api/v1/auth/oidc/providers` который возвращает список зарегистрированных providers. FE рендерит по кнопке на provider. **2 days.**

**Q6. Chat session — single project or floating?**
**Decision:** Floating (current behavior) — chat = long-lived console, не project pane. Cross-project tool calls осознанная фича.

**Q7. Models page wire to `/v1/models`?**
**Decision:** **`[FIX]` — wire к существующему `/v1/models` endpoint через proxy.** 0.5 day.

**Q8. Settings page (kill-switches).**
**Decision:** v2. Settings page остаётся mock-first. Kill-switch — operator control plane, нужен отдельный slice. Добавить в v2 backlog.

**Q9. OIDC onboarding email notification.**
**Decision:** v1.1 — добавить SMTP transport + email шаблон. Не blocker для v1 ship (silent link documented). **`[V2]` если нет SMTP stack.**

**Q10. Sources admin `dry-run` create.**
**Decision:** v1.1 — добавить mandatory probe-gate (probe должен пройти перед save). **`[FIX]` — простая frontend валидация, 0.25 day.**

**Q11. UI для tenant-scoped keys.**
**Decision:** v1.1 — добавить `tenantProjectId` поле в create-key form + backend уже принимает его. **`[FIX]` — 0.5 day.**

**Q12. Run detail — link to journal/timeline.**
**Decision:** **`[FIX]` вместе с Q1** — endpoint возвращает events list, FE рендерит timeline tab.

**Q13. Sentry panel в scheduler view.**
**Decision:** v2. Не критично для v1 (operator видит scheduled-job + run journal).

**Q14. "Stop all my failed runs" button.**
**Decision:** v2. Multi-cancel endpoint не нужен в v1.

**Q15. Edit bootstrap admin email из UI.**
**Decision:** v2. Bootstrap admin rotation — env-driven, ручная процедура (runbook §81-100).

---

## Pricing / limits (Q16–Q24)

**Q16. Rate limits — defaults OK?**
**Decision:** Defaults OK для v1. Документировать в runbook. Per-tenant tuning — v2.

**Q17. Per-project vs global concurrency cap.**
**Decision:** Per-project остаётся. Global cap — v2 (когда появится cluster-wide scheduler).

**Q18. Budget hit → 402 или 429?**
**Decision:** **`[FIX]` — switch на 402.** HTTP semantic правильный: 402 = payment required. **0.25 day.**

**Q19. Per-call max token count.**
**Decision:** v1.1 — добавить `MaxInputTokens` / `MaxOutputTokens` в `VirtualKeyConfiguration`. **`[FIX]` — 0.5 day.**

**Q20. Run over budget → 422 / 429 / 402?**
**Decision:** **`[FIX]` — 402 для hard-budget over.** Soft-budget over → warning + продолжить. **0.5 day.**

**Q21. Tokens — prompt + completion split?**
**Decision:** v1.1 — добавить `InputCostUsdMicros` / `OutputCostUsdMicros` split в `UsageEventView`.

**Q22. API key 24h throttle — OK?**
**Decision:** OK для v1. Per-minute recency — v2 (если audit потребует).

**Q23. Proxy budget reset window.**
**Decision:** Calendar month (current). Rolling 30 days — v2 (если потребует billing).

**Q24. Soft vs hard budget.**
**Decision:** **`[FIX]` — soft = warn + emit `usage_events`, hard = deny claim.** Wire в `OrchestrationBudgetGate`. **0.5 day.**

---

## Operational (Q25–Q32)

**Q25. Restart on Postgres outage.**
**Decision:** Текущее поведение OK (retry on next interval). Circuit-breaker — v2 (если будут chronic outages).

**Q26. MinIO outage — retry or skip?**
**Decision:** Current retry OK. **Добавить metric `comuki.artifact.bundle.delay_seconds`** — alert если > 5min. **`[FIX]` — 1 day.**

**Q27. OIDC state cleanup if Redis down.**
**Decision:** OIDC sweeper DB-backed (OK). ProjectSettingsCache — **`[FIX]` — fallback на in-memory cache на 30s TTL если Redis down.** 0.5 day.

**Q28. MinIO circuit breaker.**
**Decision:** v2. Per-candidate retry OK для v1.

**Q29. Bootstrap weak password check.**
**Decision:** **`[FIX]` — добавить length + char-class check (12 chars, mixed).** 0.25 day.

**Q30. OIDC sweeper if migrator not run.**
**Decision:** **`[FIX]` — добавить startup-check: если таблица не существует → лог Critical + continue (no retry storm).** 0.5 day.

**Q31. VirtualKey deleted while in-flight.**
**Decision:** **`[FIX]` — добавить 60s grace period (keep key in memory cache 60s after deletion).** 0.5 day.

**Q32. Per-key per-day spend tracking.**
**Decision:** v1.1 — добавить `usage_events.daily` view. **`[FIX]` — решается Q18 (proxy metering #2).**

---

## Compliance / security (Q33–Q38)

**Q33. OIDC tokens storage.**
**Decision:** Server-side signed cookie, httpOnly (current). Документировать в `.agents/docs/operations/security.md` (создать). **`[FIX]` — 0.25 day.**

**Q34. Bootstrap password rotation.**
**Decision:** Manual procedure OK. v2 — automated rotation (если enterprise customer попросит).

**Q35. Tenant IDs in journal.**
**Decision:** **`[FIX]` — добавить `tenant_id` denormalized column в `RunEvent`.** Backfill существующих — best-effort. 1 day.

**Q36. OIDC link purged on user disable?**
**Decision:** **`[FIX]` — `OidcAccountLinker` проверяет `Disabled` перед link. Re-enable требует admin action.** 0.5 day.

**Q37. Audit trail для PII access.**
**Decision:** v1.1 — добавить actor context во все admin handlers. **`[FIX]` — 1 day для всех admin endpoints.**

**Q38. API key tenantProjectId change audit.**
**Decision:** v1.1 — добавить `audit_events` table + middleware для admin mutations.

---

## Multi-tenancy (Q39–Q42)

**Q39. Project → multiple tenants?**
**Decision:** v1 = project-only (single tenant per project). Multi-tenant — v2. STATE.md обновить: "MVP single-tenant; multi-tenant deferred to v2".

**Q40. API keys scope.**
**Decision:** v1 — один key = один project (или global). v2 — multi-project keys.

**Q41. Tenant deletion cascade.**
**Decision:** v1 = no tenant entity. Cascade проектов — v2.

**Q42. OIDC link → project-level?**
**Decision:** v1 — global OIDC link. v2 — per-project.

---

## Cross-cutting (Q43)

**Q43. Refuse new local user when email OIDC-linked?**
**Decision:** **`[FIX]` — `InviteUserHandler` отказывает если email уже OIDC-linked.** Возвращает 409 Conflict с reason `oidc_link_exists`. 0.5 day.

---

## Сводка решений

| Action | Count | Examples |
|---|---:|---|
| **[FIX] в v1** (немедленно) | 18 | Q1 Run detail, Q3 Cost page wire, Q7 Models wire, Q11 tenant key UI, Q18 402, Q19 token cap, Q20 soft/hard budget, Q24 budget enforcement, Q26 metric, Q27 Redis fallback, Q29 weak password, Q30 migrator check, Q31 grace period, Q33 security doc, Q35 tenant_id in journal, Q36 disabled check, Q37 actor context, Q43 refuse invite |
| **[V2]** (deferred) | 14 | Q2 bulk cancel, Q4 queue page, Q5 multi-IdP UI, Q8 settings kill-switch, Q9 email notif, Q10 dry-run, Q13 Sentry panel, Q14 stop-all, Q15 bootstrap edit, Q17 global cap, Q21 tokens split, Q22 per-min audit, Q23 rolling budget, Q25 circuit breaker, Q28 MinIO breaker, Q32 per-day spend, Q34 auto rotation, Q38 audit events, Q39-Q42 multi-tenancy |
| **OK / documented** | 11 | Q6 floating chat, Q16 rate limits, Q17 per-project cap, Q22 throttle, Q23 calendar month, Q25 retry, Q26 silent retry, Q28 per-candidate, Q34 manual rotation, Q41 no entity |

## Новые tasks для v1 backlog

Из решений создать GitHub issues:

1. **[v1.1] Run detail endpoint** — `GET /api/v1/runs/{id}` + timeline tab. (Q1, Q12)
2. **[v1.1] Wire Cost page to backend** — `GET /costs` + per-project aggregation. (Q3)
3. **[v1.1] Models page wire to `/v1/models`** (Q7)
4. **[v1.1] Multi-IdP button** — `GET /oidc/providers` + per-provider buttons. (Q5)
5. **[v1.1] Sources dry-run create** — probe-mandatory before save. (Q10)
6. **[v1.1] Tenant-scoped API keys in UI** — `tenantProjectId` field. (Q11)
7. **[v1.1] Proxy 402 instead of 429 on budget hit** (Q18)
8. **[v1.1] VirtualKey per-call max token cap** (Q19)
9. **[v1.1] Run budget enforcement: soft=warn, hard=deny → 402** (Q20, Q24)
10. **[v1.1] Artifact bundle delay metric + alert** (Q26)
11. **[v1.1] ProjectSettings Redis fallback** (Q27)
12. **[v1.1] Bootstrap weak-password check (12+ chars)** (Q29)
13. **[v1.1] OIDC sweeper startup-check (table exists)** (Q30)
14. **[v1.1] VirtualKey 60s grace period** (Q31)
15. **[v1.1] Token storage security doc** (Q33)
16. **[v1.1] TenantId column in RunEvent + backfill** (Q35)
17. **[v1.1] OIDCAccountLinker checks Disabled** (Q36)
18. **[v1.1] Actor context in admin handlers** (Q37)
19. **[v1.1] Refuse invite when email OIDC-linked** (Q43)

## Critical Fixes (Q1, Q2, Q4) — must-fix for ship

Если "до ship" = v1.0.0:
- **Q1 Run detail** — must (RunDetail page в рекламе = broken UX)
- **Q4 Projects auth** — must (security blocker — кто-угодно может create/archive любой project)

Остальное — v1.1.

---

## Что фиксим прямо сейчас (в этой сессии)

**Critical для ship:** Q1, Q4
**High priority:** Q3, Q7, Q11, Q18, Q20, Q24, Q29, Q30, Q31, Q36, Q43

Разбиваю на 3 parallel agents (по группам).
