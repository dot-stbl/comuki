# Comuki — требования к фронтенду (dashboard + chat UI)

> Слепок из `comuki-v1-scope-draft.md` + PRODUCT.md. Черновик для UI/UX и
> реализации. Источник продуктовых решений — scope-draft; этот файл — **что
> должно быть на экране** (разделы, формы, состояния, права).
>
> Стек FE (уже в репо): React 19 · Vite · TS strict · Tailwind v4 · shadcn ·
> TanStack Router/Query · Kubb из OpenAPI · Comuki DS (IBM Plex Mono, status tokens).
> Real-time: SignalR (когда BE готов); до этого — poll/Query.

**Роли UI** (RBAC, только выдача, роли в коде):  
`platform-admin` · `operator` · `project-admin` · `member` · `viewer` · `approver`

**Scope навигации:** platform-уровень vs **текущий project** (переключатель проекта в шапке / chat `/project`).

---

## 0. Информационная архитектура (IA)

| Раздел | Уровень | Кто видит (минимум) | Зачем |
|--------|---------|---------------------|--------|
| **Home / Attention** | platform или project | все auth | «Нужен ли я сейчас?» — аппрувы, эскалации, failed |
| **Runs** | project | member+ | список и карточка прогона |
| **Queue / Workers** | project | member+ | очередь work items, пул воркеров |
| **Inbox (intake)** | project | member+ | каталог тикетов + «взять в работу» |
| **Sources** | project | project-admin+ | подключения GH/GL/YT/Jira, watch-фильтры |
| **Chat** | project (контекст) | member+ | полный пульт-агент |
| **Plan approve** | project | approver / project-admin | очередь планов |
| **Knowledge** | project | member+ (если фича вкл.) | поиск/статус индекса; /init wizard |
| **Verify** | project | project-admin+ (если вкл.) | статус гейта, команды (read-mostly; edit → git) |
| **Cost / Usage** | project + platform | project-admin / operator+ | деньги, токены, бюджеты |
| **Settings — live** | project / platform | project-admin / platform-admin | квоты, флаги, approve on/off, debug, budgets |
| **Settings — git links** | project | project-admin+ | ссылка на git профилей/команд (не редактор промптов в v1?) |
| **Identity** | platform | platform-admin | users, API keys, role assignments |
| **Projects** | platform | platform-admin / operator | список проектов, create |
| **Observability** | platform | operator+ | ссылки на Grafana (борды в нашем репо) |

Chat может быть: (a) отдельный route `/chat`, (b) drawer/panel на любом экране, (c) оба. **Рекомендация:** отдельный route + компактный launcher в шапке.

---

## 1. Shell / chrome

### 1.1 Шапка

- Лого Comuki · переключатель **Project** · индикатор «attention count» · тема · user menu
- User menu: профиль, API keys (свои), logout
- Launcher **Chat**
- Статусы системы (опц.): очередь >0, proxy off, knowledge off — тихие chips

### 1.2 Сайдбар (project scope)

Home · Runs · Queue · Inbox · Sources · Chat · Knowledge? · Cost · Settings

Platform-only пункты (platform-admin/operator): Projects · Identity · Observability · Platform settings

### 1.3 Глобальные состояния

- Unauthenticated → login (local / OIDC redirect)
- No project selected → empty state «выбери или создай проект»
- Forbidden (403) → экран/toast по permission
- SignalR disconnected → banner «live updates paused» + fallback poll

---

## 2. Home / Attention

**Цель:** за 2 секунды понять, нужно ли решение человека.

### Блоки

1. **Needs you** — карточки: plan waiting approve · escalated run · failed verify · stalled worker  
2. **Running now** — компактный список active runs (status tokens)  
3. **Shortcuts** — New run · Open chat · Inbox

### Формы / действия

- Approve / Reject plan (inline)
- Stop run
- Open run detail
- «Взять в работу» из inbox preview

### Права

- Approve: `approver` или `project-admin` (+ permission `plan:approve`)
- Stop: `member+` с `run:stop`
- Viewer: только смотрит

---

## 3. Runs

### 3.1 Список

Фильтры: status (queued/running/waiting/success/failed/escalated) · source · assignee/trigger · time range · search by id/title  

Колонки: title/id · status badge · source · started · cost · attention?

Bulk: stop selected (permission)

### 3.2 Карточка run (detail)

Вкладки / секции:

| Секция | Содержание |
|--------|------------|
| Overview | цель, source link, статус, cost, pin git-ref профилей |
| Plan | граф/список work items + зависимости; approve history |
| Timeline | события lifecycle/activity/result (журнал) |
| Workers | какие контейнеры, lease/heartbeat, stall |
| Artifacts | ссылки (диффы, логи) — когда storage появится |
| Verify | результат generic-command (если фича) |
| Debug | toggle raw pi stream (live setting / flag) |

### Действия

- Stop · Escalate · Inject context (форма текста → mid-run push)  
- Re-queue / retry work item  
- Open in chat («обсудить этот run»)

### Форма Inject context

- textarea · optional target work item · submit → permission `run:inject`

---

## 4. Queue / Workers

### Queue

Таблица work items: profile · status · run link · claimed by · age  
Фильтр по profile

### Workers

Карточки/таблица: id · profile · state (idle/busy/draining) · current work · compute provider  

Действия (admin): drain · force stop (осторожно)

Empty states: «нет воркеров — scale поднимет при backlog» / «min idle = 0»

---

## 5. Inbox (admission: catalog)

Список тикетов из sources (не только native): title · source · external id · status sync  

**Действие:** кнопка **«Взять в работу»** → создаёт run (или диалог confirm + optional brief hint)

Фильтры: source · label · unmatched watch  

Права: `intake:claim` / member+

---

## 6. Sources (integrations)

### Список подключений

Карточки: GitHub · GitLab · Yandex Tracker · Jira · Native (всегда)  

Статус: connected / error / disabled

### Форма подключения source

- Тип провайдера  
- Auth (PAT / OAuth / app install — по типу; секреты не светятся после save)  
- Base URL (self-hosted GL/Jira)  
- Test connection  

### Форма Watch + filter

- Вкл/выкл watch  
- Filter expression (DSL TBD — UI: labels/projects/jql-like fields)  
- Admission mode: watch / inbox-only / both  
- Sync back: status mapping preview  

### Форма Native create ticket

- Title · body · labels · create (+ optional «сразу в работу»)

---

## 7. Chat UI

Полный агент-пульт (не только «милый бот»).

### Layout

- Список сессий (user memory aware)  
- Тред сообщений  
- Composer: textarea · slash menu · project chip · attach run?  
- Side panel (опц.): current plan · run status · slash help  

### Built-in slash (минимум для UI подсказок)

`/init` · `/project` · `/run` · `/status` · `/stop` · `/plan` · `/debug` · `/help`  

+ **custom** из git клиента — подтягивать в autocomplete (название + description)

### /init — wizard (многошаговая форма в chat или modal)

Шаги (экраны):

1. Repo / git access  
2. Compute provider  
3. Model endpoints (OpenAI/Anthropic compatible URL + secrets ref)  
4. Knowledge on/off + seed  
5. Confirm → progress stream  

### Состояния сообщений

streaming · tool-call cards · plan card with Approve/Reject · error · permission denied  

### Права

Chat tools проверяют те же permissions, что и REST (не обход RBAC).

---

## 8. Plan approve

Отдельный inbox или секция Home:

- Карточка плана: human-readable summary · список nodes (profile + brief preview) · edges  
- Approve · Reject (+ reason) · Request changes (chat deep-link)  
- Если project setting `approve=off` — секция пустая / hidden  

---

## 9. Knowledge (feature flag)

- Toggle «enabled» в project live settings  
- Search box → results  
- Index status: last docs-worker · lag · errors  
- CTA: «Run /init» or «Reindex» (admin)  
- Нет тяжёлого редактора документов в v1 (пишет docs-воркер)

---

## 10. Verify (feature flag)

- Enabled toggle  
- Read-only список команд из git (path + command preview)  
- Last results on runs (deep link)  
- Edit = «открой git клиента» / link; не полноценный YAML-editor must в v1  

---

## 11. Cost / Usage

- Project: cost per run · per day · budget remaining · top expensive runs  
- Platform (operator+): across projects · proxy usage if on  
- Форма budget: soft/hard limit · alert threshold · save (live settings)  

Статусы: over budget → banner на Home/Runs  

---

## 12. Settings

### 12.1 Live (UI → reload без git)

Формы:

| Поле | Scope |
|------|--------|
| Quotas (max concurrent workers, max runs) | project |
| Plan approve on/off | project |
| Debug stream default | project |
| Feature flags: knowledge, verify, proxy | project |
| Budgets | project |
| Pool min/max idle | project |

Save → toast «applied» / validation errors  

### 12.2 Git-linked (read + link)

- Profile repo URL + pinned ref (display)  
- Custom chat commands path  
- Admission filters path  
- «Open in git» / copy ref  

Редактор промптов в UI — **не must v1** (GitOps).

### 12.3 Platform settings (platform-admin)

- Default model endpoints templates  
- Compute provider registry  
- OIDC provider config (client id, issuer, map groups? off by default)  
- SMTP? (later)  

---

## 13. Identity (platform-admin)

### Users

Список · invite/create local · link OIDC subject · disable  

### Role assignments

Форма: subject (user | api-key) · role (fixed enum select) · scope (platform | project + project picker) · grant / revoke  

Нельзя «создать роль». Нельзя выдать выше seniority (если введём) — ошибка с текстом.

### API keys

Create → show plaintext once · name · copy · revoke  
Список: prefix · subject grants summary · last used  

---

## 14. Projects

Список · create form: name · slug · git profile repo (optional) · create  

Project switcher shared with header/chat  

---

## 15. Observability

Не встраивать Grafana iframe must.  

Страница: ссылки на борды (Runs, Workers, Cost) + «как подключить» short doc  
Deep link из run detail: «open infra logs» если есть correlation id  

---

## 16. Auth screens

- Login: local (email/password or username) + button «Continue with OIDC» (если настроен)  
- Logout  
- Forbidden / session expired  

---

## 17. Общие UI-паттерны

| Паттерн | Где |
|---------|-----|
| StatusBadge | runs, workers, sources |
| RunIdChip | везде с copy |
| Attention cards | Home |
| Empty / Loading / Error / Forbidden | каждый список |
| Confirm destructive | Stop, Revoke key, Reject plan |
| Real-time badges | SignalR presence |
| Permission-hide vs disable | hide nav; disable actions with tooltip «needs X» |

---

## 18. Матрица экранов × роли (кратко)

| Экран | viewer | member | approver | project-admin | operator | platform-admin |
|-------|--------|--------|----------|---------------|----------|----------------|
| Home / Runs view | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Stop / Inject | — | ✓ | ✓ | ✓ | ✓ | ✓ |
| Plan approve | — | — | ✓ | ✓ | —* | ✓ |
| Sources edit | — | — | — | ✓ | — | ✓ |
| Live settings | — | — | — | ✓ | ✓ | ✓ |
| Identity | — | — | — | — | — | ✓ |
| Projects create | — | — | — | — | ✓ | ✓ |

\*operator — platform ops; project approve только если assignment на project.

---

## 19. Вне scope FE v1 (явно)

- Полноценный редактор system prompts в UI  
- Встроенная Grafana  
- Playwright-driven UI tests (тесты FE: vitest+MSW)  
- Custom role builder  
- Mobile-native app  

---

## 20. Связь с устаревшим PRODUCT.md

PRODUCT.md ещё описывает фиксированные 7 стадий и «правка правил в дашборде» как must.  
Актуальная модель: **гибкий граф work items**, промпты/правила агентов — **GitOps**, live UI — квоты/флаги/бюджеты.  
При следующем проходе PRODUCT.md — синхронизировать с этим файлом и scope-draft.

---

## 21. Порядок внедрения UI (предложение)

1. Shell + Auth + Projects + Home skeleton  
2. Runs list/detail (poll) + Plan approve  
3. Inbox + Sources forms  
4. Chat (+ /init wizard)  
5. Queue/Workers + live settings  
6. Identity + Cost  
7. Knowledge/Verify panels (feature flags)  
8. SignalR live  

---

*FE requirements · из сессии scope · 2026-08-30*
