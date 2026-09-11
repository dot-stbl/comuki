# Comuki — hybrid contour deploy (dev)

## Что где живёт

```
GitHub origin (dot-stbl/comuki)      GitLab mirror (nova/projects/comuki)
─────────────────────────────        ───────────────────────────────────
master: код платформы, БЕЗ          master (= локальная ветка `hybrid`):
деплой-файлов                       код + guard + overlay:
                                      .gitlab-ci.yml, deploy/hybrid/*
guard: .gitignore игнорирует        (guard в .gitignore не даёт этим
/.gitlab-ci.yml, /deploy/hybrid/    файлам утекуть на GitHub при мерже)
```

- GitLab-пайплайн собирает образы в Harbor (`registry.hybrid.ai/comuki/…`),
  раскладывает секреты Consul → K8s и применяет dev-инфраструктуру.
- ArgoCD **AppSet** `hybrid-services-dev` сам находит `deploy/hybrid/dev.yaml`
  на master (Application `comuki-dev`, ns `comuki`, chart hybrid-service,
  `image.tag = short_sha`, auto-sync) — Application руками не создавать.
- Прод пока не подключён: когда настанет первый релиз — добавить
  `deploy/hybrid/prod.yaml` (+ AppSet `hybrid-services-prod` подхватит) и
  prod-джобы по образцу console.x.

## Синхронизация веток

```bash
# продуктивная работа → GitHub
git push origin master

# зеркало → GitLab (master защищён; push от Maintainer или снять/вернуть защиту)
git checkout hybrid
git fetch origin && git rebase origin/master   # поверх guard-коммита чисто
git push gitlab hybrid:master
```

Guard-коммит `[hybrid] chore(git): guard hybrid contour overlay from GitHub`
присутствует в обеих ветках — rebase проходит без конфликтов. Overlay-файлы
коммитятся только на `hybrid` (tracked — `.gitignore` на них не влияет,
он защищает master от случайного добавления).

## Что в оверлее

| Файл | Роль |
|------|------|
| `.gitlab-ci.yml` | entrypoint → include `deploy/hybrid/ci.yml` |
| `deploy/hybrid/ci.yml` | пайплайн: unit → migrate:validate → build:image{,:worker} → secrets:dev → ▶ migrate:dev / infra:dev / promote:dev |
| `deploy/hybrid/dev.yaml` | values общего чарта hybrid-service (AppSet подставляет tag) |
| `deploy/hybrid/host.Dockerfile` | образ хоста: `/app/host` (оркестратор) + `/app/migrator` |
| `deploy/hybrid/worker.Dockerfile` | образ воркера: Translator + pi + agents-пакеты |
| `deploy/hybrid/infra-dev.yaml` | pgvector в ns comuki (ручная джоба `infra:dev`; MinIO/OTLP — общие) |
| `deploy/hybrid/migrate-job-dev.yaml` | batch Job для `migrate:dev` (tag подставляет CI) |
| `deploy/hybrid/secrets.schema.yml` | schema секретов: имена ключей Consul-блоба |

## Инфраструктура (dev)

Решение владельца 2026-09-10: только Postgres живёт в контуре comuki,
MinIO и OTLP — общие сервисы кластера.

| Компонент | Где | Как подключён |
|-----------|-----|---------------|
| Postgres | pgvector в ns `comuki` (`infra-dev.yaml`, джоба `infra:dev`) | `COMUKI_DB` → `pgvector.comuki.svc.cluster.local:5432` |
| MinIO | **общий** object-store-01, `s3.nova.adcluster.targetix.net:9000` | `Artifacts__Endpoint` в dev.yaml; креды — Consul-блоб (`Artifacts__{Access,Secret}Key`); бакет `comuki-run-bundles` создаёт сам хост (`Artifacts__AutoCreateBucket=true`) |
| OTLP | **общий** telemetry-01, `otlp.nova.adcluster.targetix.net:4317` | `Telemetry__OtlpEndpoint` в dev.yaml |

## Шаги владельца (первый запуск)

1. **Consul-блоб значений** — `cicd/nova/secrets/dev/comuki` (схема в
   `deploy/hybrid/secrets.schema.yml`; до заполнения `secrets:dev` красная —
   это намеренный гейт «образ раньше секретов не катим»):
   - `COMUKI_DB` — полный DSN:
     `Host=pgvector.comuki.svc.cluster.local;Port=5432;Database=comuki;Username=comuki;Password=…`
     (пароль = `POSTGRES_PASSWORD` из того же блоба);
   - `COMUKI_IDENTITY_APIKEY_PEPPER`, `COMUKI_TOKEN_PEPPER` — **обязательны**:
     хост стартует с `ASPNETCORE_ENVIRONMENT=Production`, validator роняет
     бут на dev-дефолтах (issue #10 T11.4); высокая энтропия, ротация
     инвалидирует выданные API-ключи/worker-токены;
   - `COMUKI_BOOTSTRAP_ADMIN_EMAIL` / `COMUKI_BOOTSTRAP_ADMIN_PASSWORD`
     (пароль ≥ 12 символов, цифра + спецсимвол);
   - `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` — доступ к **общему** MinIO
     object-store-01 (`s3.nova.adcluster.targetix.net:9000`; выделенная
     для comuki пара access-key, хост читает их же как
     `Artifacts__{Access,Secret}Key`; владелец заводит ключ на стороне
     object-store-01);
   - `POSTGRES_USER` (= `comuki`), `POSTGRES_PASSWORD`.
2. **Пайплайн**: push на GitLab master → unit + validate + образы зелёные →
   вручную ▶ `infra:dev` → ▶ `migrate:dev` (обе ждут `KUBECONFIG_B64` в CI
   переменных — дев-скоуп, права на ns `comuki`).
3. **Argo**: AppSet подхватит dev.yaml в пределах ~3 мин (requeue); при
   желании кнопка □ `promote:dev` (app `comuki-dev`) — sync + wait Healthy.
4. **DNS**: `comuki.nova.adcluster.targetix.net` → A/CNAME на ingress IP
   (nsupdate-паттерн из `virtual.deploy.dev/docs/dns.md`); после регистрации
    smoke: `GET http://comuki.nova.adcluster.targetix.net/api/v1/health` → `{"status":"ok"}`,
    `GET /api/v1/health/ready` → 200 после старта БД.

## Известные хвосты (осознанные)

- **Slice 0 follow-up**: RBAC для worker-Jobs (service account релиза не
  умеет создавать batch/Jobs в ns) + пиннинг тега `Compute:Scale:WorkerImage`
  (сейчас падает на `:latest`); gRPC-эндпоинт воркеров указывает на общий
  8080 (`http://comuki:8080`) — проверить h2c против дефолта 5051 при
  первом живом воркере.
- `migrate:dev`/`infra:dev` применяют манифесты через kubectl из CI
  (KUBECONFIG_B64, проверка кластера) — тот же живой паттерн, что
  `secrets:materialize` у console.x; «нет kubeconfig в CI» в locked
  решениях относится к выкату приложения (helm-from-CI), который здесь
  Argo.
- pgvector 1 реплика — dev-качество; для прода — операторский контур.
  MinIO и OTLP — общие сервисы (object-store-01 / telemetry-01),
  обслуживаются вне этого репо.
- `.dockerignore` на ветке `hybrid` не исключает `agents/` (нужно для
  worker-образа) — на GitHub master строка `agents` осталась: расхождение
  зеркал только в overlay-файлах, это задумано.
