---
description: local Testcontainers runtime — Podman (Windows/WSL, Linux, macOS) vs Docker, the DOCKER_HOST/Ryuk env vars and why, troubleshooting, GitLab CI equivalent
priority: high
globs: ["tests/integration/**/*.cs", "scripts/test-env/**"]
always: true
interactive: false
---

# Local test runtime — Podman vs Docker для Testcontainers

`tests/integration/*` использует Testcontainers .NET (`Testcontainers.PostgreSql`,
плюс Keycloak/Minio контейнеры в паре сьютов). Testcontainers ходит в
Docker-API-совместимый endpoint — Docker Desktop на dev-машинах этого
проекта не установлен, вместо него endpoint отдаёт **Podman**. Это правило
документирует env-переменные, которые нужны для этого, почему они именно
такие, и как чинить, когда не работает.

Скрипт-компаньон: [`scripts/test-env/podman-up.mjs`](../../../scripts/test-env/podman-up.mjs) —
детектит/стартует Podman machine и печатает рецепт ниже. Он **никогда** не
экспортирует переменные в твой шелл (дочерний процесс не может протолкнуть
env обратно в родителя ни на одной ОС) — рецепт нужно скопировать вручную
или `eval`-нуть.

```bash
bun scripts/test-env/podman-up.mjs          # детект/старт, печать exports
bun scripts/test-env/podman-up.mjs --check  # пинг Docker API, ok/fail
```

## Quick start (Windows/WSL, Podman)

```bash
# bash / zsh / git-bash
export DOCKER_HOST=npipe://./pipe/podman-machine-default
export TESTCONTAINERS_RYUK_DISABLED=true
```

```powershell
# PowerShell
$env:DOCKER_HOST = "npipe://./pipe/podman-machine-default"
$env:TESTCONTAINERS_RYUK_DISABLED = "true"
```

```bat
:: cmd.exe
set DOCKER_HOST=npipe://./pipe/podman-machine-default
set TESTCONTAINERS_RYUK_DISABLED=true
```

Дальше — обычный прогон сьюта (xUnit v3/MTP — `dotnet run`, не `dotnet test`,
см. `AGENTS.md` §Critical Non-Obvious Patterns):

```bash
dotnet run --project tests/integration/Comuki.Modules.Identity.Integration.Migrations
```

## Почему Podman, а не Docker

Dev-машины этого проекта используют **Podman Desktop** (rootless, WSL
backend на Windows), не Docker Desktop — бинаря `docker` в системе нет.
Podman отдаёт Docker-API-совместимый endpoint (подтверждено:
`Api-Version: 1.44`, `Libpod-Api-Version: 5.8.6` на Podman 5.8.2), с
которым Testcontainers .NET работает так же, как с настоящим Docker
daemon — правок кода или пакетов не требуется, меняется только адрес
endpoint-а.

## Две env-переменные, и почему

### `DOCKER_HOST` — и ловушка с числом слэшей

Testcontainers .NET (через `Docker.DotNet`) нужно явно сказать, где
Docker-API-совместимый endpoint; без этого он падает обратно на
`npipe://./pipe/docker_engine` (пайп Docker Desktop, которого здесь нет), и
каждый тест с контейнером падает на первом же `container.StartAsync()`.

**Значение — ровно два слэша после `npipe:`, не четыре:**

```
npipe://./pipe/podman-machine-default
```

Четырёхслэшевая форма, которую обычно приводят для Docker Desktop /
docker-compose (`npipe:////./pipe/docker_engine`), **не** та, что принимает
`Docker.DotNet`. При её передаче падает уже на конструировании первого
контейнера:

```
System.InvalidOperationException: The endpoint is not a npipe URI.
   at DotNet.Testcontainers.Configurations.TestcontainersSettings.get_OS()
```

Проверено эмпирически 2026-09-23 на пакете `Testcontainers.PostgreSql` этого
репозитория: 4-слэшевая форма валит все тесты в
`Comuki.Modules.Identity.Integration.Migrations` с исключением выше;
2-слэшевая проходит все 8. `scripts/test-env/podman-up.mjs` всегда печатает
именно 2-слэшевую форму — не переписывай вручную 4-слэшевую со страниц
документации Docker Desktop.

**Имя пайпа выводится, а не предполагается.** `podman machine inspect
<name>` отдаёт реальный пайп в `ConnectionInfo.PodmanPipe.Path`
(`\\.\pipe\<name>`, по конвенции совпадает с именем машины, но это не
гарантия) — скрипт читает это значение, а не хардкодит
`podman-machine-default`, так что переименованная или не-дефолтная машина
тоже работает через `--machine=<name>`.

### `TESTCONTAINERS_RYUK_DISABLED=true`

Ryuk — это reaper-контейнер Testcontainers: он следит за session ID и
убивает любой помеченный им контейнер, если тестовый процесс умер, не
подчистив за собой. Он **не обязателен для корректности здесь**: каждая
fixture в этом репо чистится явно (`IAsyncLifetime.DisposeAsync` всегда
зовёт `container.DisposeAsync()`), так что Ryuk — страховка на случай
падения прогона, а не основной путь очистки.

Отключён по умолчанию по двум причинам:
- **Один контейнер меньше на прогон** — сам Ryuk это контейнер
  `testcontainers/ryuk`, которому нужно стартовать, зарегистрироваться и
  потом самоликвидироваться по idle-таймауту. На плотном inner loop
  (повторные `dotnet run --project tests/integration/...`) это чистый
  оверхед. Эмпирически он отработал корректно против
  `podman-machine-default` (стартовал, убрал целевые контейнеры и сам
  удалился в течение ~15с после выхода тестового процесса) — на этом
  сетапе он не «сломан». Отключение — выбор скорости/простоты, а не обход
  подтверждённого здесь сбоя.
- По (не воспроизведённым в этом репо, но известным) сообщениям сообщества
  Ryuk менее стабилен под **rootless** Podman, чем под Docker или rootful
  Podman — reaper-у нужно обратное соединение со стороны daemon-а, а
  rootless network namespacing может это усложнять. Если увидишь зависание
  или застрявший `testcontainers-ryuk-*` контейнер — это первое, на что
  думать. `TESTCONTAINERS_RYUK_DISABLED=true` обходит весь этот класс
  проблем целиком.

Если Ryuk нужен (например, отлаживаешь сценарий очистки после падения
процесса) — просто не выставляй переменную, он работает. По умолчанию не
оставлять его включённым — причины см. выше.

### `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE` — здесь не нужен, но важно знать зачем

Эта переменная говорит контейнеру, **которому самому нужно обращаться к
Docker API** (например Ryuk, или docker-outside-of-docker паттерн), какой
путь к сокету использовать **внутри его собственного mount namespace**,
когда он отличается от пути, которым .NET-клиент достучался до него самого.
Актуально, когда тестовый процесс сам работает **внутри контейнера**, а
сокет daemon-а примонтирован по другому пути, чем видит хост.

Любой workflow в этом репозитории гоняет `dotnet run` напрямую на хосте
(или в раннере GitLab CI, не вложенно в контейнере, зовущем другой
container runtime), так что собственные прогоны тестов этого репо эту
переменную не используют никогда. Здесь она задокументирована потому, что
спека WS11 прямо называет её, и потому, что это ответ на случай, если
тесты всё же запускаются из контейнера против этой же Podman machine.

## Rootful vs rootless

`podman-machine-default` — **rootless** (`Rootful: false` в `podman machine
inspect`). Rootless важен для:
- Сокет daemon-а **внутри** WSL VM — это
  `unix:///run/user/1000/podman/podman.sock` (user-scoped, не
  `/run/podman/podman.sock`) — не имеет отношения к Windows-стороннему
  `DOCKER_HOST` выше (это отдельный Windows named pipe, который ставит сам
  Podman machine как прокси), но это значение нужно, если зашёл в VM через
  `podman machine ssh` и хочешь достучаться до API изнутри напрямую.
- Межконтейнерная сеть идёт через rootless network namespace VM — это тот
  же класс вещей, из-за которого Ryuk (см. выше) и резолв имён между
  контейнерами (см. WSL2 DNS ниже) иногда требуют обходных путей, которые
  под rootful daemon-ом были бы не нужны.

## Troubleshooting

| Симптом | Причина | Фикс |
|---|---|---|
| `InvalidOperationException: The endpoint is not a npipe URI` | `DOCKER_HOST` в 4-слэшевой форме `npipe:////./pipe/...` | Использовать 2-слэшевую форму: `npipe://./pipe/podman-machine-default` |
| Тесты висят на `container.StartAsync()`, ошибки долго нет, потом обрыв соединения | `DOCKER_HOST` не выставлен, либо Podman machine остановлена | `podman machine list` — если `Running` false, `podman machine start podman-machine-default` (или просто перезапустить `podman-up.mjs`, он это делает сам) |
| После прогона в `podman ps -a` висит `testcontainers-ryuk-*` | Ryuk остался включён под rootless Podman | `TESTCONTAINERS_RYUK_DISABLED=true` (рецепт по умолчанию выше); подчистить зависший вручную `podman rm -f <id>` |
| OIDC/Keycloak интеграционные тесты падают на таймауте подключения к Postgres, «no Docker DNS, only localhost bridge» | Известная проблема WSL2 Docker-DNS — контейнер не мог резолвить имя другого контейнера через bridge-сеть в конфигурации той песочницы | Ранее обходилось через `[Fact(Skip = ...)]` (см. историю `tests/integration/Comuki.Host.Integration.Oidc/OidcKeycloakShould.cs`, re-enabled 2026-09-14, когда появился рабочий runtime). Если повторится: сначала убедиться, что `podman-up.mjs --check` проходит (исключить «daemon вообще недоступен»), потом проверить, не зависит ли конкретный сьют от того, что один контейнер достаёт другой по имени/хостнейму, а не через опубликованный порт хоста — именно этот межконтейнерный путь исторически ломался в WSL2 NAT, а не путь хост→контейнер. |
| `podman-up.mjs` печатает «no Podman machine found» | Машина ни разу не создавалась | Один раз `podman machine init`, потом перезапустить |
| `podman-up.mjs --check` падает по таймауту | `Running: true`, но пайп не отвечает (машина ещё стартует или упала) | `podman machine stop <name> && podman machine start <name>`, потом `--check` снова |

## Linux / macOS

Не проверялось на этой Windows-песочнице — задокументировано по
собственным конвенциям Podman, свериться со своим сетапом:

- **Нативный Linux, rootless Podman**: `DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock`
  (при отсутствии `XDG_RUNTIME_DIR` — `/run/user/$(id -u)/podman/podman.sock`).
  Если там ничего не слушает:
  `systemctl --user enable --now podman.socket` (постоянно) или
  `podman system service --time=0 unix:///run/user/$(id -u)/podman/podman.sock &`
  (разово, на текущую сессию).
- **macOS, Podman machine**: та же форма, что на Windows, минус named
  pipe — `podman machine inspect` отдаёт
  `ConnectionInfo.PodmanSocket.Path`, использовать
  `DOCKER_HOST=unix://<этот путь>`.
- **Обычный Docker** (Docker Desktop / Colima / OrbStack, что доступно):
  дополнительные env-переменные не нужны — Testcontainers сам находит
  дефолтный Docker-сокет. `TESTCONTAINERS_RYUK_DISABLED` всё равно имеет
  смысл выставлять на rootless/VM-backed альтернативе Docker по тем же
  причинам, что и для Podman выше.

`podman-up.mjs` реализует эту ветку (детект `podman machine` для macOS,
детект `$XDG_RUNTIME_DIR/podman/podman.sock` для нативного Linux, фолбэк
на обычный Docker), но прогонялся тестами только на Windows в рамках этой
задачи — POSIX-ветку считать «верной по документированному контракту
Podman», а не «проверенной здесь независимо».

## GitLab CI equivalent

По `openspec/changes/add-agentic-test-contour/design.md` («CI job
layout»), `test:integration` и `test:agent-loop` гоняются на **раннере с
docker/podman-тегом**, `$NOVA_RUNNER_DOCKER_TAG`:

```yaml
test:integration:
  stage: test-e2e
  tags: [$NOVA_RUNNER_DOCKER_TAG]
  script: [bun run scripts/ci/dotnet-test.mjs --tier=integration]
```

У этого раннера уже есть рабочий Docker/Podman daemon и свой `DOCKER_HOST`,
подключённый на уровне раннера (вне этого репозитория) — CI-джобам
`podman-up.mjs` и exports выше не нужны; это забота **только локальной
разработки**. Если CI-джобе понадобится воспроизвести локальный сбой,
первый вопрос — Docker или Podman у раннера и rootful он или нет — от
этого зависит, какая строка этого документа применима.

`$NOVA_RUNNER_DOCKER_TAG` — открытый вопрос самого design.md («Open
Questions» — «confirm with the platform-runner owner before wiring
test-e2e/qa stages»); это правило его не закрывает, только документирует,
что предполагает форма джобы, когда тег появится.

## Проверенный рецепт (запись)

Зафиксировано 2026-09-23, Podman 5.8.2, `podman-machine-default` (WSL
backend, rootless), против
`tests/integration/Comuki.Modules.Identity.Integration.Migrations`
(`Testcontainers.PostgreSql`, `postgres:16-alpine`):

```
DOCKER_HOST=npipe://./pipe/podman-machine-default
TESTCONTAINERS_RYUK_DISABLED=true
```

`dotnet run --project tests/integration/Comuki.Modules.Identity.Integration.Migrations`
→ 8/8 passed, ~22с. Подтверждено рабочим (медленнее, лишний контейнер) и
с не выставленным `TESTCONTAINERS_RYUK_DISABLED` — флаг это дефолт ради
скорости/простоты, а не обязательный обход подтверждённого сбоя именно на
этом сетапе (см. выше). Ни один прогон не оставил осиротевших контейнеров
(`podman ps -a` чист после обоих).

## Связанные правила

- `scripts/test-env/podman-up.mjs` — скрипт, который документирует это правило
- `AGENTS.md` §Critical Non-Obvious Patterns — `dotnet run --project`, не
  `dotnet test` (xUnit v3/MTP)
- `.agents/rules/process/build-verification.md` — build gate, который это
  правило не меняет
- `openspec/changes/add-agentic-test-contour/design.md` — «Local dev
  commands», «CI job layout» (ветка `feat/agentic-test-contour-spec`)
- `tests/integration/Comuki.Host.Integration.Oidc/OidcKeycloakShould.cs` —
  история skip/re-enable по WSL2-DNS, упомянутая выше
