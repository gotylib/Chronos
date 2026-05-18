# Chronos.Master.Web

**Веб-интерфейс** Chronos: React (Vite), Tailwind, вызовы REST Chronos.Master (`/api/...` и UI v1).

## Скрипты

- **`npm install`** — зависимости.
- **`npm run dev`** — локальная разработка; прокси на URL Master (см. `vite.config`).
- **`npm run build`** — прод-сборка в каталог, который копируется в `Chronos.Master/wwwroot/ui` при сборке Docker/CI.

## Структура

- **`src/`** — страницы (дашборд, проекты, сеть, TCP routing / HAProxy, песочница Fluent), общие компоненты, клиент API.
- Соответствие экранов бэкенду описано в `docs/ARCHITECTURE.md` (раздел Master.Web).

В продакшене UI открывается с того же хоста, что и Master, по пути **`/ui`**.
