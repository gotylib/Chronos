# Chronos (prototype)

`Chronos` — прототип инструмента для управления `docker-compose`:
- конфигурация генерируется через Fluent API (`Chronos.Core`)
- конфигурация валидируется и используется для локального теста
- удалённое управление происходит через агент-сервер (`Chronos.Agent`) по HTTP
- есть консольный CLI (`Chronos.Cli`) и Polyglot/Dotnet Interactive notebook для функционального теста

> Важно: это стартовая версия. Поддержка обратного “точного” декомпозинга compose обратно в Fluent API сделана best-effort и не покрывает все поля `docker-compose`.

## Структура

В репозитории основные проекты:

- `src/Chronos.Core` — библиотека
  - fluent-модели и билд compose: `ComposeBuilder`, `ServiceBuilder`
  - генерация `docker-compose.yml`: `ComposeBuilder.GenerateYaml() / SaveToFileAsync(...)`
  - валидация: `ComposeBuilder.ValidateAsync(...)` (внутри вызывает internal `ComposeValidator`)
  - локальная оркестрация: `ComposeBuilder.StartAsync / TestAsync / StopAsync` (внутри использует `LocalTester`)
  - удалённая оркестрация: `ComposeBuilder.PublishAsync / StartRemoteAsync / RestartRemoteAsync / StopRemoteAsync` (внутри использует `HttpDeployAgent`)
  - загрузка compose с агента и best-effort декомпозиция: `ComposeBuilder.LoadFromAgentAsync(...)` + `ToFluentApiCode(...)`
  - best-effort парсер compose YAML: `ComposeYamlParser`
  - декларативные проверки/Jobs (`UseChecks` / `UseJobs`), **кодовые тесты** (класс + методы с `[Test]`, `UseTests(typeof(MyTests))`), tar-артефакты, клиент снимков volume (`HttpDeployAgent` + методы на `ComposeBuilder`)

- `src/Chronos.Agent` — агент-сервер (Minimal API)
  - хранит compose в папках проектов на сервере
  - умеет запускать/останавливать/перезапускать проект через `docker-compose`
  - отдаёт список проектов и возвращает compose по `projectName`
  - манифест/артефакты/volumes/diagnostics: `AgentRoutes`, `SchedulerHostedService`, `AgentPersistence`

- `src/Chronos.Cli` — консольный CLI (ручная проверка “end-to-end”)
  - генерирует sample compose, валидирует, опционально запускает локальный тест и/или пушит на агент

- `src/SampleTests` — пример сборки с классом `NginxTest` и методом с `[Test]`; подключение: `.UseTests(typeof(NginxTest))` или `.UseTests<NginxTest>()`

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
- `CHRONOS_AGENT_DOCKER_COMPOSE_EXECUTABLE` (default: `docker-compose`)
- `CHRONOS_AGENT_DOCKER_EXECUTABLE` (default: `docker`) — для снимков/восстановления volumes
- `CHRONOS_AGENT_ARCHIVE_IMAGE` (default: `alpine:latest`) — образ с `tar` для бэкапа volume
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
  (валидиция → upload compose → старт через агент → при наличии: `chronos/manifest.json` и tar артефактов)
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

### Chronos: манифест, артефакты, диагностика
- `POST /projects/{projectName}/chronos/manifest` — тело: JSON (`ProjectManifest`), сохраняется в `.chronos/manifest.json`
- `POST /projects/{projectName}/chronos/artifacts` — multipart, поле `archive`: tar с файлами в корень каталога проекта (потоковая распаковка; на Linux выставляется `UnixFileMode` из tar, если есть)
- `GET /projects/{projectName}/chronos/diagnostics` — последние прогоны проверок/Jobs (`DiagnosticsSnapshot`)

После успешного `start` / `restart` агент выполняет **startup-тесты** из манифеста (`onStartup: true`). Фоновый планировщик (каждые ~30 с) запускает тесты и Jobs с `intervalMinutes`, пишет результат в `diagnostics.json`; при падении теста/job с `criticality: critical` выполняется `docker-compose restart <service>`.

### Docker volumes: снимок и восстановление (потоково)

Снимок: `docker run … tar czf -` (стрим в HTTP, без загрузки всего архива в память процесса Chronos). Восстановление: стрим архива в `docker run -i … tar xzf -`.

- `GET /projects/{projectName}/volumes/{volumeName}/snapshot?compress=gzip|none` — ответ: поток `application/gzip` или `application/x-tar`
- `POST /projects/{projectName}/volumes/{volumeName}/snapshot/upload` — JSON `VolumeSnapshotUploadRequest` (`uploadUrl`, опционально `method`, `headers`, `compress`) — агент стримит снимок в указанный URL
- `POST /projects/{projectName}/volumes/{volumeName}/restore?compress=gzip|none` — multipart, поле `archive`
- `POST /projects/{projectName}/volumes/{volumeName}/restore-url` — JSON `VolumeRestoreFromUrlRequest` (`downloadUrl`, опционально `headers`, `compress`)

`volumeName` — **полное** имя Docker volume (часто `имяпроекта_имяvolume` из compose). Для консистентных бэкапов БД останавливайте сервис или используйте режимы согласованности на стороне СУБД.

Клиент (`HttpDeployAgent`): `SnapshotVolumeToFileAsync`, `SnapshotVolumeUploadToUrlAsync`, `RestoreVolumeFromFileAsync`, `RestoreVolumeFromUrlAsync`, `GetDiagnosticsAsync`, `UploadManifestJsonAsync`, `UploadArtifactsTarAsync`. На `ComposeBuilder`: обёртки `SnapshotRemoteVolumeToFileAsync` / `RestoreRemoteVolumeFromUrlAsync` и т.д.

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

## Fluent: проверки (`UseChecks`), кодовые тесты (`UseTests`), Jobs и артефакты

На сервисе:

```csharp
.AddService(s => s
    .WithName("web")
    .UseImage("nginx:alpine")
    .AddPort(8080, 80)
    .UseChecks(new DeclarativeCheck
    {
        Id = "home",
        Type = "http",
        Url = "http://127.0.0.1:8080/",
        ExpectedStatus = 200,
        OnStartup = true,
        IntervalMinutes = 60,
        Criticality = TestCriticality.Warning
    })
    .UseJobs(new JobDefinition
    {
        Id = "noop",
        Type = "exec",
        ExecCommand = "true",
        IntervalMinutes = 120,
        Criticality = TestCriticality.Info
    }))
```

Типы **декларативных** проверок: `http` (`Url`, `ExpectedStatus`), `exec` (`ExecCommand` внутри контейнера через `docker-compose exec`). Jobs: `exec` или `script` (`ScriptRelativePath` относительно каталога проекта на агенте).

### Тесты как код (C#)

Отдельная сборка `net8.0` со ссылкой на `Chronos.Core`: класс с публичными методами, помеченными `[Test]`. Сигнатура: `Task` или `Task<CodeTestOutcome>`; параметры только `ComposeTestContext` и/или `CancellationToken`. В `ComposeTestContext` есть `Http`, `ExecInServiceAsync(...)`, имена проекта/сервиса и пути compose.

Подключение **конкретного класса** (все его `[Test]` попадут в манифест; DLL того же проекта уйдёт в tar при публикации):

```csharp
using SampleTests;
// …
.UseTests(typeof(NginxTest))
// или
.UseTests<NginxTest>()
```

При `PublishAsync` DLL попадает на агент; агент и `LocalTester` загружают её через `AssemblyLoadContext` и вызывают указанные методы. **Ограничения:** версия `Chronos.Core` у тестовой сборки должна совпадать с той, с которой собран агент; зависимости кроме `Chronos.Core` нужно копировать рядом (`AddDeployArtifactFromFile` или папка). Загрузка чужих DLL — только из доверенных источников.

На `ComposeBuilder`: `AddDeployArtifactFromFile(...)` / `AddDeployArtifactFromDirectory(..., unixFileMode: 420)` — упаковываются в tar при `PublishAsync` (потоковая запись файлов). Локально после `up` startup-проверки гоняются в `LocalTester` (результаты в `TestResult.CheckRuns`); при `Critical` локальный `TestAsync` считается неуспешным.

Дополнительно: `compose.PushManifestAndArtifactsAsync(agentUrl, apiKey)` — только манифест + артефакты без старта compose.

## Roadmap / план развития

### Уже сделано (база)

- кастомные **проверки** / **Jobs** в модели + `.UseChecks` / `.UseJobs`, манифест на агенте, startup + фоновый планировщик, `criticality` с `docker-compose restart` на агенте, `GET .../chronos/diagnostics`
- **кодовые тесты**: класс + `[Test]`, `.UseTests(typeof(...))` / `.UseTests<T>()`, загрузка DLL локально и на агенте, периодический запуск через `intervalMinutes` в записи `CodeTestEntry` в манифесте
- **артефакты** проекта (tar + распаковка, Unix mode из tar на Linux)
- **volumes**: снимок/заливка по URL/восстановление из файла или URL, потоковый tar+gzip без буферизации всего архива в RAM Chronos

### Compose ↔ Fluent API

- расширить `ComposeYamlParser`: больше полей и вариантов синтаксиса `docker-compose`
- полный (или максимально полный) декомпозинг volumes, target/mode, long-syntax volume mounts в `ToFluentApiCode(...)`
- унифицировать терминологию и сигнатуры API локально и на агенте (`start` / `test` / `publish` / `stop` и т.д.), чтобы не путать сценарии

### Дальше по Chronos

- изолированная загрузка сборок (`AssemblyLoadContext` + разрешение зависимостей без «засорения» default-контекста)
- расширить диагностику (хвосты логов, агрегат по контейнеру в одном ответе со `status`)
- опционально zstd для volume-снимков, pre-signed URL и контрольные суммы в `VolumeOperationResult`

### Мелкие технические долги

- при необходимости: поддержка Compose V2 (`docker compose`) параллельно с `docker-compose`, если это важно для окружений
- явный контракт ошибок и кодов для всех новых эндпоинтов

