# Архитектура Chronos

Этот документ описывает, **какие части репозитория за что отвечают** и как они связаны. Детали API см. в Swagger Master (`/swagger`) и в комментариях кода у ключевых типов.

## Общая схема

```
┌─────────────────┐     HTTP/API      ┌─────────────────┐
│  Chronos.Master │ ◄──────────────► │ Chronos.Agent    │
│  (координация,  │                  │ (compose на хосте,│
│   UI API, БД)   │                  │  docker.sock)    │
└────────┬────────┘                  └─────────────────┘
         │
         │ статика / SPA
         ▼
┌─────────────────┐
│ Master.Web      │  (опционально: `npm run dev` — отдельная разработка UI)
│ React + Vite    │
└─────────────────┘

        Chronos.Core — библиотека: Fluent API → YAML, валидация, локальный/удалённый запуск
```

- **Chronos.Core** — общая логика без привязки к конкретному хостингу: описание compose через Fluent API, генерация и парсинг YAML, валидация, локальные команды `docker compose`, HTTP-клиент к агенту для публикации и управления проектами на удалённой машине.
- **Chronos.Agent** — процесс на машине с Docker: хранит проекты на диске, выполняет `docker compose` по запросам Master или напрямую, отдаёт compose-файлы, статус, архивы volumes, диагностику.
- **Chronos.Master** — процесс «центра»: PostgreSQL (агенты, аудит), опционально генерация HAProxy TCP-конфига, выбор лидера при нескольких экземплярах, прокси к агентам, REST для UI и интеграций, раздача собранного SPA из `wwwroot/ui`.
- **Chronos.Master.Web** — фронтенд (React): дашборд, проекты, сеть, TCP routing (HAProxy), «песочница» Fluent и т.д.; в проде собирается в статику Master.

## Модуль `src/Chronos.Core`

| Область | Файлы / типы | Назначение |
|--------|----------------|------------|
| Fluent API и сборка | `Compose/Implementation/ComposeBuilder`, `ServiceBuilder`, интерфейсы в `Compose/Interfaces/` | Цепочка вызовов `.Service(...).WithPort(...)` → готовый compose. |
| YAML | `ComposeBuilder.GenerateYaml`, `SaveToFileAsync`, `ComposeYamlParser` | Генерация и best-effort разбор `docker-compose.yml`. |
| Валидация | `ComposeValidator`, `ComposeBuilder.ValidateAsync` | Проверка конфигурации перед запуском. |
| Локальный запуск | `LocalTester`, методы `StartAsync` / `TestAsync` / `StopAsync` на билдере | Тест на той же машине, где выполняется код. |
| Удалённый агент | `HttpDeployAgent`, `DeployAgents`, методы `PublishAsync`, `StartRemoteAsync`, … | HTTP к Chronos.Agent. |
| Кластер | `ClusterClient`, публикация в кластер (`PublishToClusterAsync` и связанное в `ComposeBuilder`) | Реплики и координация через Master/агентов. |
| Граф compose | `ComposeGraphModels`, `ComposeBuilder.DescribeGraph` | Структура сервисов/сетей/volumes для UI и диагностики. |
| Декларативные проверки и jobs | `DeclarativeCheckRunner`, `CodeJobRunner`, `ComposeExec`, атрибуты в `CodeJobs` / `CodeTesting` | Jobs и кодовые тесты с `[Test]`. |
| Безопасность команд | `Safety/CommandSafety`, `AssemblySafety`, `LogRedactor` | Ограничение опасных операций и редактирование логов. |
| Прочее | `ComposeHost`, `ReplicaPolicy`, `ManifestModels`, `DeployArtifacts` | Хостинг сценариев, политики репликации, манифесты и артефакты. |

Библиотека публикуется как NuGet-пакет `Chronos.Core` (см. workflow в `.github/workflows`).

## Модуль `src/Chronos.Agent`

- **Точка входа:** `Program.cs` — регистрация Minimal API, путей к каталогу приложения и compose-файлу, клиента к Master (регистрация агента, heartbeat), сервисов планировщика и персистентности.
- **API:** `Api/` — маршруты для списка проектов, загрузки/скачивания compose, start/stop/restart, логов, volumes, снимков и т.д.
- **Домен и данные:** `Domain/`, `Infrastructure/Persistence` — EF Core (PostgreSQL или SQLite для dev), метаданные архивов volume.
- **Фоновые задачи:** планировщик проверок/jobs, синхронизация с Master по необходимости.

Переменные окружения задают путь к проектам (`CHRONOS_AGENT_APP_PATH`), имя compose-файла, URL Master, API-ключи и пути к CLI Docker/Compose.

## Модуль `src/Chronos.Master`

- **Точка входа:** `Program.cs` — PostgreSQL через `DbContextFactory`, миграции при старте, Swagger, статические файлы, fallback SPA на `/ui`, фоновая очистка устаревших агентов и старого аудита.
- **API:** `Api/MasterApiExtensions` — REST для агентов и операций кластера; `UiV1Extensions` — эндпоинты для React UI (граф compose, HAProxy TCP-маршруты, Fluent preview, прокси к агентам); `SandboxV1Extensions` — изолированный запуск Fluent-скриптов (Roslyn), если включено.
- **Инфраструктура:** `Infrastructure/Persistence` — модели и репозитории Master; `Infrastructure/Proxy` — реестр TCP-маршрутов и генерация фрагмента HAProxy на диск.
- **Сервисы:** лидер-выбор (`LeaderElection*`), репликация volumes (`VolumeReplicationHostedService`).

## Модуль `src/Chronos.Master.Web`

- **Сборка:** Vite + React + Tailwind; выход — статика, копируемая в `Chronos.Master/wwwroot/ui` при сборке образа/Dockerfile.
- **В разработке:** `npm run dev` с прокси на порт Master — UI без пересборки DLL.

Страницы соответствуют разделам: обзор кластера, проекты и тома, карта сети Docker, TCP routing (HAProxy), песочница Fluent.

## `deploy/docker`

- **`docker-compose.yml`** — поднимает Postgres (две БД), HAProxy, образы Master и Agent; типичный способ «поднять весь стек» локально.
- **`Dockerfile.master` / `Dockerfile.agent`** — многоэтапная сборка .NET и (для Master) фронта.
- **`postgres/*.sql`** — инициализация БД при первом запуске контейнера Postgres.

## Поток данных при типичном сценарии

1. Пользователь или CI описывает инфраструктуру через **Chronos.Core** (Fluent) или правит YAML вручную на агенте.
2. **Publish** отправляет файлы на **Agent** (или через Master в сценарии кластера).
3. **Master** хранит, какие агенты живы, при необходимости генерирует фрагмент **HAProxy** для TCP и проксирует запросы UI к агентам.
4. **Agent** выполняет `docker compose` и отдаёт статус, логи и метаданные volumes обратно.

## Связанные документы

- Корневой `README.md` — быстрый старт, переменные окружения, скрипты.
- `docs/ROADMAP.md` — направления развития.
