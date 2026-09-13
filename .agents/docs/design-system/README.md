# Архив: прежний визуальный мир Comuki

> ⚠️ **Здесь нет действующих решений.** Вся папка — история: дизайн-система
> первого года, построенная поверх shadcn/ui и Tailwind v4. shadcn вырезан
> из продукта целиком (175 файлов, 14 зависимостей, вместе с recharts), и
> возвращать его запрещено — см. `DESIGN.md` § Components.

## Куда смотреть вместо этой папки

| Что нужно | Действующий источник |
|-----------|----------------------|
| Палитра, темы, типографика, формы, статусы, компоненты | [`DESIGN.md`](../../../DESIGN.md) в корне репозитория |
| Токены, которые читает продукт | [`dashboard/src/app/styles/tokens.css`](../../../dashboard/src/app/styles/tokens.css) |
| Реестр тем | `dashboard/src/app/theme/themes.ts` → генерируемый `themes.css` |
| Кит примитивов | [`dashboard/src/shared/ui/AGENTS.md`](../../../dashboard/src/shared/ui/AGENTS.md) |
| Процесс сборки UI, no-slop, FE-гейт | [`../../rules/coding/frontend-construct-rules.md`](../../rules/coding/frontend-construct-rules.md) |

`PRODUCT.md` § Brand Commitments фиксирует это же: «визуальный мир заменён и
зафиксирован», а эта папка «годится как история, но не как источник решений».

## Что здесь лежит

| Файл | Что это | Почему устарело |
|------|---------|-----------------|
| `Comuki Design System.md` | Полное описание прежней системы | Описывает `globals.css` как shadcn drop-in, содержит Tailwind v4 mapping и рецепт «как пересобрать shadcn-компонент»; ссылается на несуществующий `dashboard/src/shared/ui/primitives/` |
| `Comuki shadcn Components.html` | Живая галерея shadcn-компонентов | Библиотека удалена из продукта; галерея показывает то, чего в коде нет |
| `Comuki Foundation.html`, `Comuki Dashboard.html` | Мокапы прежних экранов | Прежняя палитра и прежняя вёрстка |
| `styles/tokens.css`, `styles/globals.css`, `styles/components.css` | Архивные токены и стили | Не те, что читает продукт. Действующие — `dashboard/src/app/styles/` |
| `dashboard/*.jsx`, `dashboard/dashboard.css`, `dashboard/*.js` | Прототип дашборда | Прототип, не код продукта |

## Правило для агента

Ничего отсюда **не переносить в код**, не сверять с этим токены и не
«восстанавливать» описанные здесь компоненты. Если задача требует значения
токена, цвета, шрифта или радиуса — читать `DESIGN.md` и
`dashboard/src/app/styles/tokens.css`, а не эту папку.

Удалять файлы тоже не надо: они хранят, **почему** система выглядела так и
из чего выросла нынешняя.
