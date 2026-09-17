# shared/ui — порт-плейбук Storybook → Comuki kit

Процедура переноса / написания компонента в продакшн-кит `shared/ui`.
Storybook = единственный источник правды по виду/UX. «Почему» —
[ADR-0001](../../../../.agents/docs/architecture/adr-0001-ui-kit-react-aria.md).

## 0. Источники правды

- **Вид/поведение** — **Storybook** (`bun run storybook`, :6006):
  - `UI Kit/*` — стори кита (`shared/ui/<name>.stories.tsx` и со-локализованные
    доменные стори).
  - Исторические `UI/*` / `Comuki/*` (legacy shadcn demos из `_legacy/`)
    удалены вместе с карантином.
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
- **Фокус — два разных рецепта, не путать.**
  `outline: var(--hairline) solid var(--ring)` — кольцо на существующей грани,
  доминирующий рецепт кита. Мягкое двухпиксельное кольцо — это
  `box-shadow: var(--shadow-focus-ring)` (или `--shadow-focus-ring-inset`
  внутрь, если коробка зажата в скроллере), а `--ring-wash` — тот же цвет
  отдельно, для `outline: 2px solid var(--ring-wash)`. Рукописного
  `color-mix(in oklab, var(--ring) 35%, transparent)` быть не должно.
- **Leading — четыре режима, пятого нет:** `--lh-flat` (высоту задаёт не текст:
  кнопка, chip, rail-item), `--lh-tight` (1–3 слова с переносом: бейдж,
  ячейка), `--lh-display` (крупный кегль), `--lh-body` (проза). Сырой
  `line-height: 1.2` — нарушение.
- **z-index — общая шкала** `--z-base/raised/sticky/dock/dropdown/overlay/
  modal/popover/tooltip`. Все `.scrim` кита стоят на `--z-overlay`, и порядок
  между двумя оверлеями решает DOM, а не число. Компонент, который открывает
  **свой** stacking context, шкалу не берёт и держит приватную лестницу — так
  делает `data-table` (`--dt-z-*`) и `::after` кнопки под `isolation: isolate`.
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

## 6. Strangler / `_legacy` — REMOVED

- `_legacy` удалён (вместе с showcase-роутом `/components`, legacy-стори
  из `src/stories/`, path-шимами `shared/ui/<name>.tsx` и shadcn-депами:
  radix-*, cva, recharts, vaul, embla, cmdk, input-otp, next-themes,
  react-day-picker, date-fns, @base-ui/react, shadcn CLI). Правило
  «не удалять» снято — strangler завершён.
- Импортировать `_legacy` больше нечего и неоткуда: путь не существует.
  Если встретил импорт `_legacy` — это битая ссылка, чини на кит.
- Новый код импортирует **только** `@/shared/ui` (barrel) — как и раньше.
- `_legacy` не возвращать: новые примитивы — только китом (CSS Modules +
  React Aria), по процедуре этого плейбука.
- **Внимание:** «strangler завершён» относится к shadcn-компонентам и их
  зависимостям, **не к Tailwind**. Tailwind жив и остаётся — см. §6a.

## 6a. Tailwind v4 — слой сборки живой, вырезать нельзя

Формулировка §6 («shadcn вырезан целиком») уже один раз была прочитана как
«и Tailwind тоже» — агент пошёл его удалять. Поэтому явно:

**Tailwind v4 остаётся в проекте и удалению не подлежит.** Владелец репо
пробовал убрать — «всё поплыло». Это не обсуждается и не является техдолгом,
который кто-то должен закрыть.

Не трогать:

- `dashboard/vite.config.ts` — плагин `@tailwindcss/vite`;
- `dashboard/src/index.css` — `@import "tailwindcss"`, `@import "tw-animate-css"`,
  `@custom-variant dark`, блок `@theme inline`;
- `dashboard/.prettierrc` — `prettier-plugin-tailwindcss`;
- `dashboard/package.json` — `tailwindcss`, `@tailwindcss/vite`, `tailwind-merge`.

### Что при этом верно

**В компонентном коде утилит-классов нет и заводить их нельзя.** Правило §2
(«БЕЗ Tailwind / `cva` / shadcn в новом коде») остаётся в силе полностью:
вид компонента — CSS Module на токенах, `className="flex gap-2"` в `.tsx` —
нарушение. Проверено грепом: сейчас таких классов в `dashboard/src` ноль, и
это состояние надо удержать.

Живым остаётся **слой сборки**, а не словарь классов:

- `@tailwindcss/vite` компилирует `index.css`;
- `@layer base` в `index.css` держит несколько базовых сбросов через `@apply`;
- `twMerge` внутри `cn()` (`shared/lib/utils.ts`) — да, мёржить сейчас нечего,
  но `cn()` вызывается из 84 файлов, и его сигнатура — не место для экономии.

### `@theme inline` — мост, и он несущий

Блок `@theme inline` в `index.css` — это то, чем токены из `tokens.css` видны
Tailwind-слою. Его уже пробовали вырезать: вёрстка плывёт.

**Следствие, важное при чистке токенов:** токен, у которого нет потребителей
в `.module.css`/`.tsx`, но который упомянут внутри `@theme inline`, —
**живой**. Это весь `--sidebar-*`, весь `--chart-*`, `--*-foreground`,
`--input`, `--card-*`, `--popover-*`. Аудит записал их в «мёртвый груз» — это
ошибка аудита, а не находка.

Мёртвым токен считается **только** если на него нет ссылок ни в `src/**`,
**ни в `index.css`**. Грепать надо оба места.

## 7. Чеклист перед merge

1. Триада: `.tsx` + `.module.css` + `.stories.tsx`.
2. Экспорт в `index.ts`.
3. Только `var(--…)` в CSS Module.
4. Простой vs React Aria — классификация верна.
5. `bun run typecheck && bun run lint && bun run test` — exit 0.
