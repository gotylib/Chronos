import { useEffect, useRef, useState } from "react";
import {
  Background,
  Controls,
  ReactFlow,
  ReactFlowProvider,
  type Edge,
  type Node
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import type { AgentInfo, HaproxyTcpRoute, HaproxyTcpRoutesResponse } from "../api/client";
import { addHaproxyTcpRoute, deleteHaproxyTcpRoute, getAgents, getHaproxyTcpRoutes } from "../api/client";

export function RoutingPage() {
  const [payload, setPayload] = useState<HaproxyTcpRoutesResponse | null>(null);
  const [agents, setAgents] = useState<AgentInfo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [backendHost, setBackendHost] = useState("host.docker.internal");
  const [backendPort, setBackendPort] = useState("");
  const [listenPort, setListenPort] = useState("");
  const [agentId, setAgentId] = useState("");
  const [projectName, setProjectName] = useState("");
  const [busy, setBusy] = useState(false);
  const suggestedApplied = useRef(false);

  const graph =
    payload != null ? buildMasterAgentTcpGraph(payload.masterPublicHost, payload.routes, agents) : { nodes: [], edges: [] };

  function refresh() {
    setError(null);
    Promise.all([getHaproxyTcpRoutes(), getAgents()])
      .then(([tcp, ag]) => {
        setPayload({
          dynamicDirectory: tcp.dynamicDirectory ?? null,
          masterPublicHost: tcp.masterPublicHost ?? null,
          routes: tcp.routes ?? [],
          generatedCfg: tcp.generatedCfg ?? null,
          suggestedBackendHost: tcp.suggestedBackendHost ?? null,
          suggestedBackendPort: tcp.suggestedBackendPort ?? null,
          listenPortMin: tcp.listenPortMin ?? null,
          listenPortMax: tcp.listenPortMax ?? null
        });
        setAgents(ag);
      })
      .catch((e: Error) => setError(e.message));
  }

  useEffect(() => {
    refresh();
  }, []);

  useEffect(() => {
    if (!payload || suggestedApplied.current) return;
    suggestedApplied.current = true;
    if (payload.suggestedBackendHost) {
      setBackendHost(payload.suggestedBackendHost);
      if (payload.suggestedBackendPort != null) setBackendPort(String(payload.suggestedBackendPort));
    }
  }, [payload]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (backendPort.trim() === "") throw new Error("Укажите backend port — тот, что в compose в «ports:» слева (на хосте).");
      const bp = Number(backendPort);
      if (!Number.isFinite(bp) || bp < 1 || bp > 65535) throw new Error("backendPort must be 1–65535");
      const lp = listenPort.trim() === "" ? undefined : Number(listenPort);
      if (listenPort.trim() !== "" && (!Number.isFinite(lp) || lp! < 1 || lp! > 65535))
        throw new Error("listenPort must be empty or 1–65535");

      await addHaproxyTcpRoute({
        name: name.trim() || undefined,
        backendHost: backendHost.trim(),
        backendPort: bp,
        listenPort: lp,
        agentId: agentId.trim() || undefined,
        projectName: projectName.trim() || undefined
      });
      setName("");
      setListenPort("");
      refresh();
    } catch (err) {
      setError(String(err));
    } finally {
      setBusy(false);
    }
  }

  const routes = payload?.routes ?? [];

  return (
    <ReactFlowProvider>
      <div className="space-y-8">
        <div>
          <h1 className="text-2xl font-semibold text-white">TCP routing (HAProxy)</h1>
          <p className="mt-1 max-w-3xl text-sm text-slate-400">
            Это <strong>TCP-прокси</strong>: снаружи подключаются к <strong>listen</strong> (порт на машине с HAProxy, см. проброс в compose), HAProxy
            пересылает трафик на <strong>backend</strong>. Контейнеры, которые поднимает агент через Docker, слушают на{" "}
            <strong>хосте</strong> в портах из <code className="text-emerald-300">ports:</code> слева. Из контейнера HAProxy до хоста в dev используйте{" "}
            <code className="text-emerald-300">host.docker.internal</code> и этот <strong>хостовый</strong> порт — не имя сервиса compose проекта и не{" "}
            <code className="text-amber-200">chronos-agent</code>, если речь не про сам API Chronos. Мастер только пишет cfg; HAProxy в compose уже с{" "}
            <code className="text-slate-400">extra_hosts: host.docker.internal</code>.
          </p>
          {payload?.dynamicDirectory ? (
            <p className="mt-2 font-mono text-xs text-slate-600">
              Dynamic dir: <span className="text-slate-400">{payload.dynamicDirectory}</span>
            </p>
          ) : (
            <p className="mt-2 text-xs text-amber-400/90">
              <code className="text-amber-200">CHRONOS_HAPROXY_DYNAMIC_DIR</code> не задан — добавление маршрутов отключено.
            </p>
          )}
          {payload?.listenPortMin != null && payload?.listenPortMax != null ? (
            <p className="mt-2 text-xs text-slate-500">
              Авто listen — только порты{" "}
              <span className="font-mono text-slate-300">
                {payload.listenPortMin}–{payload.listenPortMax}
              </span>
              , проброшенные у сервиса <code className="text-slate-400">haproxy</code> в compose.
            </p>
          ) : null}
          <div className="mt-3 rounded-lg border border-sky-900/40 bg-sky-950/20 p-3 text-xs text-sky-100">
            После добавления или удаления маршрута перезагрузите HAProxy, чтобы подхватить новые{" "}
            <code className="text-sky-200">listen</code>, например:{" "}
            <span className="font-mono text-[11px] text-sky-200">
              docker compose -f deploy/docker/docker-compose.yml kill -s HUP haproxy
            </span>
            . В репозитории для dev проброшен диапазон <code className="text-sky-200">5002–5099</code> на haproxy; без
            этого listen-порты не видны с хоста. Статистика HAProxy:{" "}
            <span className="font-mono">http://127.0.0.1:18404/stats</span>.
          </div>
        </div>

        {error ? (
          <div className="rounded-lg border border-red-900/50 bg-red-950/30 p-3 text-sm text-red-200">{error}</div>
        ) : null}

        <form onSubmit={submit} className="max-w-xl space-y-4 rounded-xl border border-slate-800 bg-slate-950/50 p-5">
          <h2 className="text-sm font-semibold text-white">Добавить TCP-маршрут</h2>
          <label className="block text-xs text-slate-500">
            Имя (подпись, необязательно)
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-white"
              placeholder="postgres-prod"
            />
          </label>
          <label className="block text-xs text-slate-500">
            Backend host (для контейнеров агента на том же Docker-хосте — <code className="text-emerald-300">host.docker.internal</code>; удалённый
            агент — IP/hostname машины с Docker)
            <input
              value={backendHost}
              onChange={(e) => setBackendHost(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-emerald-200"
            />
          </label>
          <label className="block text-xs text-slate-500">
            Backend port (левое число в <code className="text-slate-500">ports:</code> compose, то что открыто на хосте)
            <input
              value={backendPort}
              onChange={(e) => setBackendPort(e.target.value)}
              placeholder="например 15432"
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-white"
            />
          </label>
          <label className="block text-xs text-slate-500">
            Listen port на мастере (пусто — подобрать свободный от backend-порта вверх)
            <input
              value={listenPort}
              onChange={(e) => setListenPort(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-white"
              placeholder="оставить пустым для авто"
            />
          </label>
          <label className="block text-xs text-slate-500">
            Agent id (для подписи на схеме, необязательно)
            <input
              value={agentId}
              onChange={(e) => setAgentId(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-white"
            />
          </label>
          <label className="block text-xs text-slate-500">
            Project name (необязательно)
            <input
              value={projectName}
              onChange={(e) => setProjectName(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-white"
            />
          </label>
          <button
            type="submit"
            disabled={busy || !payload?.dynamicDirectory}
            className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-600 disabled:opacity-50"
          >
            Add route
          </button>
        </form>

        <section className="space-y-3">
          <h2 className="text-sm font-semibold text-white">Master → listeners → agent backends</h2>
          <div className="h-[440px] rounded-xl border border-slate-800 bg-slate-950">
            {graph.nodes.length === 0 ? (
              <div className="flex h-full items-center justify-center text-sm text-slate-500">
                Нет маршрутов — добавьте TCP proxy или включите CHRONOS_HAPROXY_DYNAMIC_DIR.
              </div>
            ) : (
              <ReactFlow nodes={graph.nodes} edges={graph.edges} fitView nodesDraggable={false} nodesConnectable={false}>
                <Background color="#1e293b" gap={18} />
                <Controls className="!border-slate-700 !bg-slate-900" />
              </ReactFlow>
            )}
          </div>
        </section>

        <section>
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold text-white">Текущие маршруты</h2>
            <button type="button" onClick={() => refresh()} className="text-xs text-emerald-400 hover:text-emerald-300">
              Refresh
            </button>
          </div>
          {routes.length === 0 ? (
            <div className="text-sm text-slate-500">Пусто.</div>
          ) : (
            <ul className="space-y-3">
              {routes.map((r) => (
                <li key={r.id} className="rounded-xl border border-slate-800 bg-slate-950/60 px-4 py-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="font-mono text-xs text-emerald-300">
                      {payload?.masterPublicHost ?? "master"}:{r.listenPort} → {r.backendHost}:{r.backendPort}
                      {r.name ? ` · ${r.name}` : ""}
                    </div>
                    <button
                      type="button"
                      className="text-xs text-red-400 hover:text-red-300"
                      onClick={async () => {
                        if (!confirm("Удалить этот TCP-маршрут?")) return;
                        await deleteHaproxyTcpRoute(r.id);
                        refresh();
                      }}
                    >
                      Delete
                    </button>
                  </div>
                  <div className="mt-1 text-[11px] text-slate-500">
                    {r.agentId ? `agent ${r.agentId}` : "agent —"}
                    {r.projectName ? ` · project ${r.projectName}` : ""}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </section>

        {payload?.generatedCfg ? (
          <section>
            <h2 className="mb-2 text-sm font-semibold text-white">Сгенерированный chronos-tcp.cfg</h2>
            <pre className="max-h-72 overflow-auto rounded-xl border border-slate-800 bg-slate-950 p-4 font-mono text-[11px] text-slate-400">
              {payload.generatedCfg}
            </pre>
          </section>
        ) : null}
      </div>
    </ReactFlowProvider>
  );
}

function buildMasterAgentTcpGraph(
  masterPublicHost: string | null | undefined,
  routes: HaproxyTcpRoute[],
  agents: AgentInfo[]
): { nodes: Node[]; edges: Edge[] } {
  const nodes: Node[] = [];
  const edges: Edge[] = [];
  const seen = new Set<string>();

  const hostLabel = masterPublicHost?.trim() || "Master";
  const masterId = "node-master";

  const pushNode = (id: string, label: string, color: string, x: number, y: number) => {
    if (seen.has(id)) return;
    seen.add(id);
    nodes.push({
      id,
      data: { label },
      position: { x, y },
      style: {
        background: "#0f172a",
        color: "#e2e8f0",
        border: `2px solid ${color}`,
        borderRadius: 10,
        padding: 10,
        width: 210,
        fontSize: 11,
        whiteSpace: "pre-wrap"
      }
    });
  };

  pushNode(masterId, `${hostLabel}\nHAProxy TCP edge`, "#38bdf8", 20, 140);

  const agentLookup = new Map(agents.map((a) => [a.agentId, a]));

  routes.forEach((r, i) => {
    const y = i * 200 + 30;
    const listenId = `listen-${r.id}`;
    const backendId = `backend-${r.backendHost}-${r.backendPort}-${r.id}`;

    const agentInfo = r.agentId ? agentLookup.get(r.agentId) : undefined;
    const agentSubtitle = agentInfo
      ? `${agentInfo.agentId}\n${agentInfo.baseUrl}`
      : r.agentId
        ? `${r.agentId}`
        : `${r.backendHost}`;

    pushNode(listenId, `Listen TCP\n:${r.listenPort}`, "#34d399", 300, y);
    pushNode(
      backendId,
      `Agent backend\n${agentSubtitle}\n${r.backendHost}:${r.backendPort}${r.projectName ? `\nproject: ${r.projectName}` : ""}`,
      "#f59e0b",
      580,
      y
    );

    edges.push({
      id: `e-m-${r.id}`,
      source: masterId,
      target: listenId,
      label: "expose"
    });
    edges.push({
      id: `e-l-${r.id}`,
      source: listenId,
      target: backendId,
      label: `forward`
    });
  });

  return { nodes, edges };
}
