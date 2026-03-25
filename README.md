# Chronos (prototype)

`Chronos` — прототип инструмента для управления `docker-compose`:
- конфигурация генерируется через Fluent API (`Chronos.Core`)
- конфигурация валидируется и используется для локального теста
- удалённое управление происходит через агент-сервер (`Chronos.Agent`) по HTTP
- есть консольный CLI (`Chronos.Cli`) и Polyglot/Dotnet Interactive notebook для функционального теста

> Важно: это стартовая версия. Поддержка обратного “точного” декомпозинга compose обратно в Fluent API сделана best-effort и не покрывает все поля `docker-compose`.

## Структура

В репозитории есть 3 проекта:

- `src/Chronos.Core` — библиотека
  - fluent-модели и билд compose: `ComposeBuilder`, `ServiceBuilder`
  - генерация `docker-compose.yml`: `ComposeBuilder.GenerateYaml() / SaveToFileAsync(...)`
  - валидация: `ComposeBuilder.ValidateAsync(...)` (внутри вызывает internal `ComposeValidator`)
  - локальная оркестрация: `ComposeBuilder.StartAsync / TestAsync / StopAsync` (внутри использует `LocalTester`)
  - удалённая оркестрация: `ComposeBuilder.PublishAsync / StartRemoteAsync / RestartRemoteAsync / StopRemoteAsync` (внутри использует `HttpDeployAgent`)
  - загрузка compose с агента и best-effort декомпозиция: `ComposeBuilder.LoadFromAgentAsync(...)` + `ToFluentApiCode(...)`
  - best-effort парсер compose YAML: `ComposeYamlParser`

- `src/Chronos.Agent` — агент-сервер (Minimal API)
  - хранит compose в папках проектов на сервере
  - умеет запускать/останавливать/перезапускать проект через `docker-compose`
  - отдаёт список проектов и возвращает compose по `projectName`

- `src/Chronos.Cli` — консольный CLI (ручная проверка “end-to-end”)
  - генерирует sample compose, валидирует, опционально запускает локальный тест и/или пушит на агент

## Требования

- Docker Engine
- `docker-compose` (CLI `docker-compose`, не compose plugin)
- .NET SDK

## Быстрый старт (локально)

### 1) Запуск агента

В отдельной консоли:

```powershell
dotnet run --project "src/Chronos.Agent/Chronos.Agent.csproj" -- --urls http://0.0.0.0:5000
```

По умолчанию агент хранит всё в `/app` и использует compose-файл `docker-compose.yml`.

Опционально:
- `CHRONOS_AGENT_APP_PATH` (default: `/app`)
- `CHRONOS_AGENT_COMPOSE_FILE` (default: `docker-compose.yml`)
- `CHRONOS_AGENT_API_KEY` (если задан — нужно слать `X-API-Key` заголовок)

### 2) Локальная проверка через CLI

```powershell
dotnet run --project "src/Chronos.Cli/Chronos.Cli.csproj" -- --sample nginx --compose-out docker-compose.yml --local-test --project-name chronos
```

CLI:
- генерирует compose через Fluent API
- валидирует (валидация встроена в `TestAsync`)
- делает `docker-compose up -d`, ждёт контейнеры/health/running
- на фейле печатает tail-логи
- по умолчанию делает `down -v`

## Local API: управление compose как объектом

Минимальная идея: создаёшь `ComposeBuilder`, задаёшь `.WithProjectName(...)` и сервисы, дальше управляешь.

Пример (псевдо-code):

```csharp
var compose = new ComposeBuilder()
    .WithProjectName("chronos-demo")
    .AddNetwork("web-net")
    .AddService(s => s.WithName("web").UseImage("nginx:alpine").AddPort(8080, 80));

await compose.StartAsync();   // Validate + Save compose to docker-compose.yml + up -d (контейнеры остаются)
await compose.TestAsync();    // Validate + up -d + ожидание + down -v
await compose.StopAsync();    // down (можно removeVolumes)
```

Что делает `ComposeBuilder`:
- `ValidateAsync(...)` — проверяет корректность модели
- `StartAsync(...)` — `ValidateAsync` → сохраняет YAML → `docker-compose up -d` и **не удаляет** контейнеры
- `TestAsync(...)` — `ValidateAsync` → `up -d` → ожидание/health → `down -v` (если `RemoveAfterTest=true`)
- `StopAsync(removeVolumes: true)` — `docker-compose down -v`

## Remote API: агент по `projectName`

Ты не должен передавать путь на своей машине на агент.
Логика такая:
- библиотека генерирует YAML
- отправляет на агент под `projectName`
- агент кладёт compose в свою директорию: `/app/<projectName>/docker-compose.yml`
- агент запускает/останавливает проект, выполняя `docker-compose -f docker-compose.yml ...` **из папки проекта**

### Публикация / запуск / стоп

- `await compose.PublishAsync(agentUrl, apiKey)`  
  (валидиция → upload compose → старт через агент)
- `await compose.StartRemoteAsync(agentUrl, apiKey)`  
  (валидция → start для проекта на агенте)
- `await compose.RestartRemoteAsync(agentUrl, apiKey)`  
  (валидция → restart для проекта на агенте)
- `await compose.StopRemoteAsync(agentUrl, apiKey, removeVolumes: true/false)`  
  (stop для проекта)

`agentUrl` — это base URL агента, например: `http://localhost:5000`.

## Агент: HTTP эндпоинты

### Список проектов
- `GET /projects`

### Работа с compose по проекту
- `GET /projects/{projectName}/compose`
- `POST /projects/{projectName}/compose` (multipart form-data поле `compose`)

### Управление проектом
- `POST /projects/{projectName}/start` (multipart form-data поле `compose` опционально)
- `POST /projects/{projectName}/stop?removeVolumes=true|false`
- `POST /projects/{projectName}/restart?removeVolumes=true|false` (compose опционально)
- `GET /projects/{projectName}/status`
- `GET /projects/{projectName}/logs?service={containerServiceName}`

### Авторизация

Если задан `CHRONOS_AGENT_API_KEY`, то все запросы требуют заголовок:
- `X-API-Key: <apiKey>`

## Восстановление compose в Fluent API (best-effort)

Библиотека умеет:
- скачать compose YAML с агента
- распарсить в `ComposeBuilder`
- попытаться сгенерировать “похожий на исходный” fluent-код

Примеры (библиотечные методы):
- `ComposeBuilder.ListRemoteProjectsAsync(agentUrl, apiKey)`
- `ComposeBuilder.LoadFromAgentAsync(agentUrl, projectName, apiKey)`
- `builder.ToFluentApiCode("compose")`

Ограничения:
- volumes/deep options ещё не восстанавливаются “идеально”
- не все варианты compose синтаксиса поддержаны парсером

## Polyglot Notebook (ручной тест)

В репозитории лежит:
- `Chronos.PolyglotNotebook.ipynb`

Там заранее добавлены `#r "nuget: ..."` для зависимостей (`YamlDotNet`, `Docker.DotNet`, `Polly`).

Рекомендованный порядок:
1) `Restart kernel` (если меняли DLL/зависимости)
2) выполнить ячейку с `dotnet build`
3) выполнить ячейку с `#r nuget: ...` и `#r Chronos.Core.dll`
4) потом уже запускать `await compose.StartAsync() / TestAsync() / StopAsync()`

## TODO / Следующие шаги (уже очевидные)

- улучшить `ComposeYamlParser` (больше полей `docker-compose`)
- добавить полный декомпозинг volumes/volume targets/modes в fluent-код
- унифицировать API по терминологии (start/test/publish/stop для agent)

