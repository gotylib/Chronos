# Chronos Development Roadmap

Это дорожная карта, основанная на плане из обсуждения. Она разделена на фазы, где приоритетом выступает безопасность и минимальный “master+agents” контур, который можно поднимать быстро и воспроизводимо.

## Phase 0 — Security Hardening (Critical)

### 0.1 Resource Limits & Sandboxing
- Таймауты выполнения тестов/jobs (настраиваемо, по умолчанию до 30 секунд)
- Ограничение параллельных запусков (на проект/на агент)
- Валидация опасных операций в командном режиме (через blacklist/whitelist для exec/script)
- Assembly validation (signature checks + грубая эвристика опасных API)

### 0.2 Audit & Monitoring
- Audit log для операций с `API Key` (кто/что/когда/результат)
- Alerting на паттерны подозрительных действий

### 0.3 Environment Isolation
- Изоляция окружения выполнения для тестов/jobs
- Redact sensitive env vars в логах

## Phase 1 — Distributed Architecture (Master Node)

### 1.1 Master Node Core
- `Chronos.Master` Minimal API
- Agent Registry
  - регистрация агентов при старте
  - heartbeat (примерно раз в 30 секунд)
  - отбрасывание неотвечающих агентов
- Хранилище метаданных (SQLite на старте)

### 1.2 Smart Scheduler
- выбор агента по ресурсам/latency/affinity к volumes/cost
- сбор метрик и простые прогнозы на основе истории

### 1.3 Unified API
- расширение `ComposeBuilder` для master-ориентированных деплоев
- models для `GlobalDeploymentRequest/Response`

## Phase 2 — Distributed Storage & Volumes

### 2.1 Volume Manager
- DistributedVolumeManager: локализация volumes на агентах и поиск
- replication factor (1/2/3), восстановление, promotion primary

### 2.2 Backup & Restore
- бэкапы по расписанию, retention policy
- cross-agent restore streaming

### 2.3 Shared Storage Integration
- NFS/Ceph/AWS EBS/Azure Managed Disks

## Phase 3+ (HA, Networking, Observability, Enterprise Features)

Остаются как в исходном плане: Raft multi-master, failover/self-healing, service discovery/load balancing/network policies, observability (Prometheus/Grafana), UX (UI) и enterprise (RBAC, Vault, GitOps, DR).

## MVP статус (сейчас в репозитории)

- Реализован мастер MVP контур “Agent Registry”:
  - `POST /agents/register`
  - `POST /agents/{agentId}/heartbeat`
  - `GET /agents`
  - TTL чистка агентов по параметру `CHRONOS_MASTER_AGENT_TTL_SECONDS`
- Реализован агентский механизм, чтобы при наличии env переменных мастер автоматически получал регистрацию и heartbeat:
  - `CHRONOS_MASTER_URL`
  - `CHRONOS_AGENT_BASE_URL`
  - опционально: `CHRONOS_MASTER_API_KEY`, `CHRONOS_AGENT_ID`, `CHRONOS_AGENT_LOCATION`

- Реализована Phase 0 isolation/safety на агенте и локально:
  - таймаут выполнения тестов/jobs: `CHRONOS_AGENT_TEST_EXECUTION_TIMEOUT_SECONDS` (по умолчанию 30с)
  - ограничение параллельности: `CHRONOS_AGENT_MAX_PARALLEL_TESTS_PER_PROJECT` (по умолчанию 5) и `CHRONOS_AGENT_MAX_PARALLEL_TESTS_TOTAL` (по умолчанию 20)
  - blacklist/whitelist безопасность для `exec/script` в тестах/jobs (env):
    - `CHRONOS_AGENT_SENSITIVE_PROJECTS`
    - `CHRONOS_AGENT_SENSITIVE_COMMAND_WHITELIST`
  - redaction секретов в логах и сообщениях об ошибках

