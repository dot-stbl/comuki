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
| `Microsoft.VisualStudio.Threading.Analyzers` (VSTHRD) | ✅ подключён | `Directory.Packages.props` + `Directory.Build.props`; хирургический набор — 200 / 002 / 104 = `error`, 103 = `none` |
| `Meziantou.Analyzer` (MA) | ❌ **удалён 2026-08-31** | шум; то, что он ловил, покрывают IDE-анализаторы |
| `Roslynator.Analyzers` (RCS) | ❌ **удалён 2026-08-31** | шум; то, что он ловил, покрывают IDE-анализаторы |

Build-флаги (уже в `Directory.Build.props`):
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — warnings = errors
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` — style rules в build
- `<AnalysisLevel>latest</AnalysisLevel>` — последние CA-правила
- `<AnalysisMode>None</AnalysisMode>` + `<AnalysisModeSecurity>All</AnalysisModeSecurity>`
  — из CA включена **только** категория Security плюс явные opt-in в `.editorconfig`

> **MA и RCS не возвращать «заодно».** Удаление 2026-08-31 — записанное
> решение (см. комментарии в `Directory.Build.props`, `Directory.Packages.props`
> и шапке `.editorconfig`), а не недосмотр.

Severity конкретных правил — в `.editorconfig` (~50 записей).

## Когда подключать analyzer к одному проекту

Если по какой-то причине analyzer нужен **только** одному проекту
(не глобально), в его `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="<Analyzer.Package>" PrivateAssets="all" />
</ItemGroup>
```

Версия — в `Directory.Packages.props` (CPM включён,
`ManagePackageVersionsCentrally=true`), в csproj её не дублировать.
Roslynator и Meziantou в качестве такого примера **не использовать** — они
удалены осознанно.

`PrivateAssets="all"` обязательно — иначе analyzer утечёт в
runtime-зависимости (зависимость попадёт в `nuget package` и т.д.).

## Что делать при новых warnings

1. **Починить код** — предпочтительный путь (warnings = code smell).
2. **Подавить локально** — `#pragma warning disable CA1822 // <почему>`
   с комментарием-обоснованием + `#pragma warning restore CA1822`.
3. **Глобально опустить severity** — в `.editorconfig`:
   `dotnet_diagnostic.CA1822.severity = none` + запись в `.agents/STATE.md`.

**Запрещено**: править `.editorconfig` ради одного файла, добавлять
`<NoWarn>` в csproj без записи в `.agents/STATE.md`.

## Чеклист: добавить новый analyzer package

1. Добавить `<PackageVersion Include="..." Version="..." />` в `Directory.Packages.props`
2. Добавить `<PackageReference Include="..." PrivateAssets="all" />` в `Directory.Build.props` (для всех) ИЛИ в `.csproj` (для одного)
3. Прогнать `dotnet build comuki.slnx` — посмотреть новые warnings
4. Разобрать warnings: починить / подавить с обоснованием / baseline
5. Коммит отдельным `chore(analyzers): add <package>`

## Связанные правила

- `.editorconfig` — severity каждого правила
- [`CODING-RULES.md`](CODING-RULES.md) §15 — required tooling
- [`../process/build-verification.md`](../process/build-verification.md) — как прогонять build
