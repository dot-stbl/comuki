# @comuki/dev-sdk — Claude Code SDK для разрабов

Сидит в IDE разработчика (не в контейнере). Три вещи:

1. **Lock gate** — `PreToolUse` hook: те же замки, что у воркеров
   (`@comuki/agent-core` `BLOCKED_TOOL_TARGETS`), механикой Claude Code.
2. **Session context** — `SessionStart` hook: инжектит Comuki-контекст
   (проект, каталог профилей, memory digest).
3. **MCP client** — тонкая обёртка к Comuki Knowledge по MCP.

Runtime — **Claude Code** (external). Семантика замков шарится с
`comuki-worker-sdk` через `@comuki/agent-core` — разъехаться не может.

## Install

```bash
cd agents && bun install

# патчит ~/.claude/settings.json (backup: *.comuki-backup.json, один раз)
bun comuki-dev-sdk/src/install.ts
# или в конкретный файл
bun comuki-dev-sdk/src/install.ts --settings path/to/settings.json

# удалить наши hook-и (чужие не трогаем)
bun comuki-dev-sdk/src/install.ts --uninstall
```

Идемпотентно: повторный install — no-op. Чужие hooks / model / permissions
не меняются.

## Hook config (что пишет installer)

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash|Edit|Write|MultiEdit|NotebookEdit",
        "hooks": [
          { "type": "command", "command": "bun \"<pkg>/src/hooks/lock-gate.ts\"", "timeout": 15 }
        ]
      }
    ],
    "SessionStart": [
      {
        "matcher": "*",
        "hooks": [
          { "type": "command", "command": "bun \"<pkg>/src/hooks/comuki-context.ts\"", "timeout": 10 }
        ]
      }
    ]
  }
}
```

## Lock gate — формат решения

Один emitter, две формы (обе блокируют вызов инструмента):

| `COMUKI_HOOK_STYLE` | Вид | Claude Code |
|---|---|---|
| `json` (default) | stdout `{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"…"}}`, exit 0 | **>= 1.0.84** (`permissionDecision`); на старых JSON игнорируется |
| `exit` | exit code **2**, причина в stderr (попадает модели) | **все** версии hooks |

Fail-open: битый stdin / нераспознанный payload → allow (сломанный гейт не
должен ломать сессию разраба).

Маппинг CC-инструментов на виды замков:

- `Edit` / `Write` / `MultiEdit` / `NotebookEdit` → `file_path` /
  `notebook_path` проверяется против `edit-path` правил;
- `Bash` → команда рендерится как `Bash(<command>)` против `tool-name`
  правил; плюс парсинг `git push` (в т.ч. `git -C dir push`,
  `cd pkg && git push origin main`, refspec `HEAD:main`) против `git-ref`
  правил.

## Lock rules (общий набор с worker-sdk)

| id | kind | pattern | причина |
|---|---|---|---|
| `no-edit-tests` | edit-path | `**/*.test.*` | тесты принадлежат платформенному гейту |
| `no-edit-spec` | edit-path | `**/*.spec.*` | — // — |
| `no-edit-tests-dir` | edit-path | `**/tests/**` | — // — |
| `no-edit-underscore-tests-dir` | edit-path | `**/__tests__/**` | — // — |
| `no-npm-install` / `no-npm-add` | tool-name | `Bash(npm install*` / `Bash(npm add*` | нет side-инсталлов |
| `no-bun-add` / `no-bun-install` | tool-name | `Bash(bun add*` / `Bash(bun install*` | — // — |
| `no-pnpm-add` / `no-pnpm-install` | tool-name | `Bash(pnpm add*` / `Bash(pnpm install*` | — // — |
| `no-yarn-add` / `no-yarn-install` | tool-name | `Bash(yarn add*` / `Bash(yarn install*` | — // — |
| `no-pip-install` | tool-name | `Bash(pip install*` | — // — |
| `no-dotnet-add-package` | tool-name | `Bash(dotnet add package*` | — // — |
| `no-push-main` | git-ref | `refs/heads/main` | нет прямых пушей в protected |
| `no-push-master` | git-ref | `refs/heads/master` | — // — |

Источник — `@comuki/agent-core` (`BLOCKED_TOOL_TARGETS`); LS движок —
`comuki-agent-core/src/locks/`.

## Env vars

| Переменная | Назначение | Default |
|---|---|---|
| `COMUKI_PROJECT` | имя проекта для контекст-хедера | — |
| `COMUKI_HOST` | базовый URL платформы (catalog `/profiles`, memory digest) | — |
| `COMUKI_MCP_URL` | MCP endpoint (включает MCP-обёртку) | — |
| `COMUKI_MCP_TOKEN` | bearer для MCP | — |
| `COMUKI_HOOK_STYLE` | `json` \| `exit` (формат решения гейта) | `json` |

## MCP

```ts
import { ComukiMcpClient } from '@comuki/dev-sdk';

const mcp = ComukiMcpClient.fromEnv();            // null без COMUKI_MCP_URL
if (mcp && (await mcp.connect())) {
  const tools = await mcp.listTools();            // ComukiTool[]
  const hit = await mcp.callTool('search', { q: 'locks' });
}
await mcp?.close();
```

- streamable-HTTP → fallback на legacy SSE;
- fail-soft: нет сети → `connect() === false`, `listTools()` → `[]`,
  `callTool()` → `null` (никогда не бросает);
- auth: `Authorization: Bearer <COMUKI_MCP_TOKEN>` на каждый запрос.

Memory digest endpoint (`GET {COMUKI_HOST}/api/v1/memory/digest?project=`) —
provisional контракт: платформенный роут приземлится в Slice 2, digest-фетч
fail-soft, так что форма URL может уточняться без поломки хука.

## Разработка

```bash
cd agents
bun run typecheck      # все 3 пакета, strict
bun test               # bun test, включая lock-gate decision table
```
