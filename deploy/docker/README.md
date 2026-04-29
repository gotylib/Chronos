# deploy/docker

Файлы для **локального и контейнерного** запуска всего стека Chronos.

| Файл | Назначение |
|------|------------|
| `docker-compose.yml` | Postgres (две БД), Traefik, сервисы `chronos-master` и `chronos-agent`; общая сеть для прокси и DNS между контейнерами. |
| `Dockerfile.master` | Сборка Chronos.Master + встраивание статики UI из `Chronos.Master.Web`. |
| `Dockerfile.agent` | Сборка Chronos.Agent с доступом к Docker через сокет хоста (как в compose). |
| `postgres/*.sql` | Начальная инициализация пользователей/БД в Postgres при первом старте тома. |

Запуск из корня репозитория:

```bash
docker compose -f deploy/docker/docker-compose.yml up --build
```

Порты и переменные см. в корневом `README.md`.
