# shared/ui — порт-плейбук Storybook → Comuki kit

Процедура переноса / написания компонента в продакшн-кит `shared/ui`.
Storybook = единственный источник правды по виду/UX. «Почему» —
[ADR-0001](../../../../.agents/docs/architecture/adr-0001-ui-kit-react-aria.md).

## 0. Источники правды

- **Вид/поведение** — **Storybook** (`bun run storybook`, :6006):
  - `UI Kit/*` — стори нового кита (`shared/ui/<name>.stories.tsx`);
  - `UI/*` / `Comuki/*` — legacy shadcn demos (из `_legacy/`, не эталон кита).
  При расхождении **прав бук**, не HTML mock из `.agents/docs/design-system/`.
- **Токены** — два файла, и они не пересекаются:
  `app/styles/themes.css` — **генерируемый**, только цветовые примитивы, по
  блоку на тему (`bun src/app/theme/gen-themes.ts`; правится реестр, не файл).
  `app/styles/tokens.css` — весь остальной словарь: производные от примитивов
  токены, шкалы отступов, типографика, радиусы, тени, длительности.
  Нет нужного токена → добавь в `tokens.css`. Нужен новый **цвет** → он
  обязан появиться во всех темах сразу, иначе тест на дрейф упадёт.
  **Никогда** не хардкодь hex/px.
- **Дизайн-спека** — `.agents/docs/design-system/Comuki Design System.md`
  (исторический вход; то, что принято, живёт в токенах + стори).

## 1. Классифицировать (ADR-0001)

- **Простой презентационный** (Button, Input, Badge, Card, StatusBadge) →
  семантический DOM + CSS Module. Без headless-библиотек.
- **Сложный интерактивный** (Dialog/Modal, Menu, Select/Combobox, Tabs,
  Tooltip/Popover, DatePicker) → **React Aria Components** для поведения/a11y
  + CSS Module для вида.

Эталон сложного примитива — `ConfirmDialog` (React Aria Modal/Dialog).

## 2. Компонент `<name>.tsx`

- named export; файл kebab-case = имя компонента.
- явный TS-тип пропсов (без `React.FC`).
- `ref` — обычный проп (React 19, без `forwardRef`).
- дискриминируемые юнионы для вариантов, не набор булевых флагов.
- `data-test` на интерактивных элементах.
- БЕЗ TanStack Query / бизнес-логики — данные через пропсы.
- БЕЗ Tailwind / `cva` / shadcn в новом коде.

## 3. CSS Module `<name>.module.css`

- Только `var(--token)` — из `tokens.css` или из примитивов `themes.css`.
- **No raw hex / px / font-family** — только токены.
- Условные классы через `cn()` (`shared/lib/utils.ts`). Без `cva`.
- Тёмная тема бесплатна: `.dark` переключает токены.

## 4. Barrel

Экспортируй из `shared/ui/index.ts`. Новый код импортирует **только**
`@/shared/ui` (barrel). Внутренности приватны.

## 5. Стори — обязательный артефакт

Со-локализованный `<name>.stories.tsx` рядом с триадой:

- `title: 'UI Kit/<Категория>/<Компонент>'` — категории: `Actions` · `Data` ·
  `Feedback` · `Inputs` · `Layout` · `Navigation` · `Overlays` · `Surfaces`.
- Файлы в `shared/ui/` **плоские** (категория только в title).
- **Исключение — составной примитив.** Если примитив это несколько компонентов,
  работающих в паре (таблица + её тулбар), он живёт в своей папке
  `shared/ui/<name>/` со своим `index.ts`, который ре-экспортится из корневого
  барреля. Внутренности папки приватны так же, как и всё остальное: домены
  импортируют только `@/shared/ui`. Плоскость — правило для одиночных
  компонентов, а не запрет на группировку.
- Одна стори на каждое значимое состояние (variants, disabled, danger).

## 6. Strangler / `_legacy`

- `shared/ui/_legacy/**` — quarantine shadcn/radix wrappers.
- Новый код **не** импортирует `_legacy`.
- Домены мигрируют по одному; временные shim-re-exports с старых путей
  допустимы только для ещё-не-портированных примитивов.
- Не удалять `_legacy` и не расширять его новыми компонентами.

## 7. Чеклист перед merge

1. Триада: `.tsx` + `.module.css` + `.stories.tsx`.
2. Экспорт в `index.ts`.
3. Только `var(--…)` в CSS Module.
4. Простой vs React Aria — классификация верна.
5. `bun run typecheck && bun run lint && bun run test` — exit 0.
