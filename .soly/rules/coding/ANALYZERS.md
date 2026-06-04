---
description: C# analyzer packages — Roslynator, Meziantou, Microsoft.CodeAnalysis.NetAnalyzers. How they are wired in this project.
globs: ["**/*.csproj", "**/Directory.Build.props", "**/Directory.Packages.props"]
always: true
---

# Analyzer packages — cheatsheet

В comuki C#-анализаторы подключены **централизованно** через
`Directory.Packages.props` + `Directory.Build.props` (Central Package
Management). `Microsoft.CodeAnalysis.NetAnalyzers` дополнительно
подключать **не нужно** — встроен в .NET 5+ SDK.

## Что уже встроено / настроено

| Пакет | Статус | Конфиг |
|-------|--------|--------|
| `Microsoft.CodeAnalysis.NetAnalyzers` (CA) | ✅ встроен в .NET SDK | `<AnalysisLevel>latest</AnalysisLevel>` в `Directory.Build.props` |
| `Microsoft.VisualStudio.Threading.Analyzers` (VSTHRD) | ✅ встроен в `Microsoft.VisualStudio.Threading.Analyzers` (тоже SDK) | используется через analyzer rules |
| `Meziantou.Analyzer` (MA) | ✅ подключён | `Directory.Packages.props` + `Directory.Build.props` |
| `Roslynator.Analyzers` (RCS) | ✅ подключён | `Directory.Packages.props` + `Directory.Build.props` |

Build-флаги (уже в `Directory.Build.props`):
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — warnings = errors
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` — style rules в build
- `<AnalysisLevel>latest</AnalysisLevel>` — последние CA-правила

Severity конкретных правил — в `.editorconfig` (~50 записей).

## Когда подключать analyzer к одному проекту

Если по какой-то причине analyzer нужен **только** одному проекту
(не глобально), в его `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Roslynator.Analyzers" Version="4.*" PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` обязательно — иначе analyzer утечёт в
runtime-зависимости (зависимость попадёт в `nuget package` и т.д.).

## Что делать при новых warnings

1. **Починить код** — предпочтительный путь (warnings = code smell).
2. **Подавить локально** — `#pragma warning disable RCS1234 // <почему>`
   с комментарием-обоснованием + `#pragma warning restore RCS1234`.
3. **Глобально опустить severity** — в `.editorconfig`:
   `dotnet_diagnostic.RCS1234.severity = none` + комментарий
   в `.planning/BACKEND-ISSUES.md` или `NEXT-STEPS.md`.

**Запрещено**: править `.editorconfig` ради одного файла, добавлять
`<NoWarn>` в csproj без записи в baseline-issue.

## Чеклист: добавить новый analyzer package

1. Добавить `<PackageVersion Include="..." Version="..." />` в `Directory.Packages.props`
2. Добавить `<PackageReference Include="..." PrivateAssets="all" />` в `Directory.Build.props` (для всех) ИЛИ в `.csproj` (для одного)
3. Прогнать `dotnet build comuki.slnx` — посмотреть новые warnings
4. Разобрать warnings: починить / подавить с обоснованием / baseline
5. Коммит отдельным `chore(analyzers): add <package>`

## Связанные правила

- `.editorconfig` — severity каждого правила
- `.soly/rules/coding/CODING-RULES.md` §15 — required tooling
- `.soly/rules/process/build-verification.md` — как прогонять build
