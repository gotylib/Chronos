# deploy/docker

Файлы для **локального и контейнерного** запуска всего стека Chronos.

| Файл | Назначение |
|------|------------|
| `docker-compose.yml` | Postgres (две БД), HAProxy, Loki, Grafana, сервисы `chronos-master` и `chronos-agent`; общий каталог `haproxy/dynamic` для `chronos-tcp.cfg`. |
| `Dockerfile.master` | Сборка Chronos.Master + встраивание статики UI из `Chronos.Master.Web`. |
| `Dockerfile.agent` | Сборка Chronos.Agent с доступом к Docker через сокет хоста (как в compose). |
| `postgres/*.sql` | Начальная инициализация пользователей/БД в Postgres при первом старте тома. |

Запуск из корня репозитория:

```bash
docker compose -f deploy/docker/docker-compose.yml up --build
```

Быстрые адреса:
- Master: `http://localhost:5000`
- Agent: `http://localhost:5001`
- Grafana: `http://localhost:3000` (`admin/admin`)
- Loki: `http://localhost:3100`
- HAProxy stats (HTTP): `http://localhost:18404/stats` (на хосте проброшен порт **18404** → **8404** внутри контейнера, чтобы реже конфликтовать с уже занятым `:8404`).

### TCP-маршруты HAProxy (прокси портов, которые агент пробросил на хост)

Агент через `docker.sock` поднимает чужие compose-проекты; их `ports:` слушают на **хосте Docker**, а не в overlay-сети Chronos. HAProxy в нашем compose до них ходит как **`host.docker.internal:<левый порт из ports>`** (на той же машине, что и Docker). У сервиса `haproxy` задан `extra_hosts: host.docker.internal:host-gateway` (нужно на Linux). Если HAProxy крутится **не** на машине агента — в маршруте укажите **сетевой адрес** агента и тот же опубликованный порт.

Порты и переменные см. в корневом `README.md`.

### Если ошибка `Bind for 0.0.0.0:18404 failed`

На машине уже что-то слушает этот порт — поменяйте левую часть в `docker-compose.yml` у сервиса `haproxy`, например `"28404:8404"`.

### Если всё ещё поднимается Traefik

В текущем репозитории сервиса Traefik **нет**. Обычно это старый стек Docker Compose с тем же именем проекта (`chronos`) или контейнеры от предыдущей версии `docker-compose.yml`.

Из каталога, откуда вы запускаете compose:

```bash
docker compose -f deploy/docker/docker-compose.yml down
docker ps -a --filter "name=chronos"
```

Удалите/остановите лишние контейнеры (`docker rm …`) и поднимите снова с **обновлённым** файлом из репозитория (`git pull`). Если нужно сбросить только сеть compose без томов:

```bash
docker compose -f deploy/docker/docker-compose.yml down --remove-orphans
```

### HAProxy на Linux: host network (все listen-порты сразу на хосте)

В **bridge**-режиме (как в `docker-compose.yml` по умолчанию) HAProxy слушает порты **внутри** контейнера; на хост их видно только если перечислить в `ports` (у нас проброшен диапазон `5000-5099`).

На **нативном Linux** можно вместо диапазона использовать **`network_mode: host`** у сервиса `haproxy`: процесс HAProxy делит сетевой стек с хостом, поэтому все строки `bind *:<listen>` из сгенерированного `chronos-tcp.cfg` сразу доступны на машине (как при установке HAProxy пакетом на хост).

Что сделать вручную в `docker-compose.yml`:

1. У сервиса **`haproxy`** добавить **`network_mode: host`** и **удалить секцию `ports:`** целиком (с `network_mode: host` проброс портов не используется; иначе Compose обычно выдаёт ошибку).
2. У **`chronos-master`** в `environment` выставить бэкенд так, чтобы HAProxy **с хоста** достучался до агента по **опубликованному** порту, например:
   - `CHRONOS_HAPROXY_TCP_SUGGESTED_BACKEND_HOST=127.0.0.1`
   - `CHRONOS_HAPROXY_TCP_SUGGESTED_BACKEND_PORT=5001`  
   (если агент на хосте слушает тот же порт, что и в compose, например `5001:5001`).
3. **Важно:** в `chronos-tcp.cfg` бэкенд не должен оставаться `chronos-agent:…` — с host network DNS имён compose **нет**. Нужны `127.0.0.1` и порт хоста (или IP хоста в LAN). После смены режима пересоздайте TCP-маршруты в UI или поправьте `chronos-tcp-routes.json` и перезагрузите HAProxy (`docker compose … exec haproxy kill -s HUP 1`).

В **bridge**-режиме к сервису `chronos-agent` указывают DNS-имя сервиса и порт, на котором Kestrel слушает **внутри** контейнера (в текущем `docker-compose.yml` это **5001**, см. `ASPNETCORE_URLS` / `Dockerfile.agent`). Проброс `ports` вида `5001:5001` дублирует тот же номер на хосте.
4. Статистика в `haproxy.cfg` слушает **8404** внутри контейнера; в host mode это станет **:8404 на хосте** (не 18404). При конфликте порта поменяйте `bind` в `deploy/docker/haproxy/haproxy.cfg` для `frontend stats`.

**Ограничение:** `network_mode: host` в **Docker Desktop / WSL** часто ведёт себя иначе, чем на нативном Linux; там обычно оставляют bridge + проброс портов.

### Linux: `host.docker.internal` для бэкенда (HAProxy остаётся в bridge)

Если бэкенд — процесс на **хосте** (порт опубликован на `localhost`), у `haproxy` можно добавить:

```yaml
extra_hosts:
  - "host.docker.internal:host-gateway"
```

и в маршрутах указывать backend `host.docker.internal` и порт на хосте. Слушатели (`listen`) по-прежнему нужно пробрасывать из контейнера HAProxy на хост **или** использовать host network только для HAProxy (см. выше).
