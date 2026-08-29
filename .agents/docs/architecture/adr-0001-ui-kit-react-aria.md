# ADR-0001 — UI-кит: свой + React Aria; уход с shadcn (strangler)

- **Статус:** Accepted
- **Дата:** 2026-08-30
- **Контекст:** dashboard mock-first UI на shadcn (radix-mira) + Tailwind v4; желание выровняться с console.x.

## Контекст

Comuki dashboard сейчас = shadcn-обёртки над `radix-ui` / точечно `@base-ui` + Tailwind utility + CVA. Визуал уже на Comuki-токенах, но **форма компонентов чужая** (shadcn recipes), агенты тянут shadcn-паттерны, расхождение с console.x (ADR-0007: свой кит + React Aria + CSS Modules, без Tailwind/shadcn).

Источник вида для Comuki уже есть: `.agents/docs/design-system/` (tokens, HTML mock, StatusBadge semantics).

## Решение

1. **Свой UI-кит** в `dashboard/src/shared/ui/` на токенах Comuki.
2. **Стили — CSS Module на компонент** (`*.module.css`), только `var(--token)`. Без Tailwind / `cva` / shadcn в новом коде.
3. **React Aria Components** — только для сложных интерактивных примитивов (Dialog, Menu, Select/Combobox, Tabs, Tooltip/Popover, DatePicker). Простые (Button, Input, Badge, Card, StatusBadge) — semantic DOM + CSS Module.
4. **Миграция — strangler:**
   - новый код → новый кит;
   - текущий shadcn → `shared/ui/_legacy/` (или alias quarantine);
   - домены мигрируют по одному (runs → approvals → …);
   - eslint boundary: domains не импортят `_legacy` в новом коде / запрет расти `_legacy`.
5. **Storybook = SSOT** по виду (как console.x). Design-system HTML — вход, не вечный референс.

## Рассмотренные альтернативы

| | Вариант | Вердикт |
|---|---------|---------|
| A | Свой + React Aria (console.x) | **Выбрано** |
| B | Оставить shadcn | отклонено — чужой visual language |
| C | Base UI + свои стили | отклонено — нет синергии с console.x |
| D | Ark/Zag | отклонено — ещё один стек в контуре |
| E | MUI/Ant/Chakra | отклонено — не ops-mono |
| F | Strangler рядом с shadcn | **способ миграции** для A, не отдельный стек |

## Последствия

**Плюсы:** контроль вида; общий язык с console.x; adherence lint; агентам явный плейбук.

**Минусы:** стоимость переписи примитивов + экранов; два мира на время strangler; нужен `shared/ui/AGENTS.md` плейбук до массового порта.

## Первые шаги (kit foundation)

1. Вынести токены в `app/styles/tokens.css` (из `index.css` / design-system).
2. Плейбук `shared/ui/AGENTS.md` (порт из console.x, адаптировать под Comuki).
3. Эталоны: `Button`, `StatusBadge`, `ConfirmDialog` (React Aria).
4. Перенести shadcn → `_legacy/`; обновить imports доменов постепенно.
5. stylelint (no raw hex/px) + eslint barrel-only для нового кита.

## Related

- console.x `frontend/docs/adr/0007-ui-design-system-react-aria.md`
- `.agents/docs/design-system/Comuki Design System.md`
- `AGENTS.md` § Design tokens / Port pool
