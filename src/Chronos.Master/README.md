# Chronos.Master

**Центральный сервис**: PostgreSQL для состояния кластера (агенты, аудит), REST API для операций и для встроенного UI, опциональная генерация **HAProxy TCP** (`CHRONOS_HAPROXY_DYNAMIC_DIR`), фоновые задачи (лидерство, репликация volumes, очистка).

## Точка входа

- **`Program.cs`** — БД и миграции, Swagger, статические файлы, SPA по пути `/ui`, регистрация эндпоинтов Master и UI v1.

## Назначение папок

| Папка | Роль |
|--------|------|
| `Api/` | `MasterApiExtensions`, `UiV1Extensions`, `SandboxV1Extensions` и связанные типы — всё, что отдаётся по HTTP. |
| `Application/` | Абстракции и сервисы (персистентность, выбор лидера и т.д.). |
| `Infrastructure/Persistence` | EF-модели Master и реализация хранилища. |
| `Infrastructure/Proxy` | HAProxy: реестр TCP-маршрутов и генерация `chronos-tcp.cfg`. |
| `wwwroot/ui/` | Собранный фронт из проекта `Chronos.Master.Web` (появляется после сборки образа или publish). |

Подробная карта — в `docs/ARCHITECTURE.md`.
