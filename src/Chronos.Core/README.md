# Chronos.Core

Библиотека **Fluent API → docker-compose**, валидация и оркестрация **локально** (через CLI Docker Compose на машине разработчика) и **удалённо** (HTTP к процессу Chronos.Agent).

## Главные точки входа

- **`ComposeBuilder`** (`Compose/Implementation/ComposeBuilder.cs`) — конфигурация сервисов, сетей, volumes, проверок, тестов и публикации на агента/в кластер.
- **`ComposeYamlParser`**, **`ToFluentApiCode`** — разбор существующего YAML и генерация кода Fluent (best-effort).
- **`LocalTester`** — локальный подъём/останов стека для проверки.

## Подпапки

| Папка / файл | Роль |
|----------------|------|
| `Compose/` | Модели compose, билдеры и контракты (`IComposeBuilder`, `IServiceBuilder`). |
| `Safety/` | Ограничения для выполняемых команд и сборок (CommandSafety, AssemblySafety, LogRedactor). |
| Остальные `.cs` в корне | Парсер YAML, валидатор, клиенты деплоя и кластера, runners для checks/jobs/tests, модели манифеста и графа compose. |

Пакет собирается как **NuGet** (`Chronos.Core`). Потребители: Chronos.Agent (ссылка на типы при необходимости), внешние приложения и тесты.
