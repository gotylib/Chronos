import { useEffect, useState } from "react";
import {
  Background,
  Controls,
  ReactFlow,
  ReactFlowProvider,
  type Edge,
  type Node
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { deleteTraefikRoute, getTraefikRoutes, upsertTraefikRoute } from "../api/client";

export function RoutingPage() {
  const [dir, setDir] = useState<string | null>(null);
  const [routes, setRoutes] = useState<{ fileName: string; yaml: string }[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [routeName, setRouteName] = useState("myapp");
  const [rule, setRule] = useState("Host(`myapp.localhost`)");
  const [backends, setBackends] = useState("http://127.0.0.1:8080");
  const [busy, setBusy] = useState(false);

  const graph = buildTraefikGraph(routes);

  function refresh() {
    setError(null);
    getTraefikRoutes()
      .then((r) => {
        setDir(r.directory ?? null);
        setRoutes(r.routes ?? []);
      })
      .catch((e: Error) => setError(e.message));
  }

  useEffect(() => {
    refresh();
  }, []);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const urls = backends
        .split(/[\n,]+/)
        .map((s) => s.trim())
        .filter(Boolean);
      await upsertTraefikRoute({ routeName, rule, backendUrls: urls });
      setRouteName("myapp");
      refresh();
    } catch (err) {
      setError(String(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <ReactFlowProvider>
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-semibold text-white">Edge routing (Traefik)</h1>
        <p className="mt-1 max-w-3xl text-sm text-slate-400">
          Пишет YAML-фрагменты в каталог file-provider (тот же volume, что смотрит Traefik в compose). Правило — синтаксис Traefik v3{" "}
          <code className="text-emerald-300">rule:</code>.
        </p>
        {dir ? (
          <p className="mt-2 font-mono text-xs text-slate-600">
            Directory: <span className="text-slate-400">{dir}</span>
          </p>
        ) : (
          <p className="mt-2 text-xs text-amber-400/90">
            CHRONOS_TRAEFIK_DYNAMIC_DIR не задан — используется no-op (файлы не пишутся).
          </p>
        )}
        <div className="mt-3 rounded-lg border border-sky-900/40 bg-sky-950/20 p-3 text-xs text-sky-100">
          В docker-compose Traefik опубликован на <span className="font-mono">http://127.0.0.1:9080</span>, не на <span className="font-mono">:8080</span>.
          Для правила <span className="font-mono">Host(`myapp.localhost`)</span> проверяйте так:
          <div className="mt-1 font-mono text-[11px] text-sky-200">
            curl -H "Host: myapp.localhost" http://127.0.0.1:9080/
          </div>
        </div>
      </div>

      {error ? (
        <div className="rounded-lg border border-red-900/50 bg-red-950/30 p-3 text-sm text-red-200">{error}</div>
      ) : null}

      <form onSubmit={submit} className="max-w-xl space-y-4 rounded-xl border border-slate-800 bg-slate-950/50 p-5">
        <h2 className="text-sm font-semibold text-white">Добавить / обновить маршрут</h2>
        <label className="block text-xs text-slate-500">
          Route name (файл chronos-route-…)
          <input
            value={routeName}
            onChange={(e) => setRouteName(e.target.value)}
            className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-white"
          />
        </label>
        <label className="block text-xs text-slate-500">
          Rule
          <input
            value={rule}
            onChange={(e) => setRule(e.target.value)}
            className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-emerald-200"
          />
        </label>
        <label className="block text-xs text-slate-500">
          Backend URLs (по одному в строке или через запятую)
          <textarea
            value={backends}
            onChange={(e) => setBackends(e.target.value)}
            rows={3}
            className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-white"
          />
        </label>
        <button
          type="submit"
          disabled={busy}
          className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-600 disabled:opacity-50"
        >
          Upsert route
        </button>
      </form>

      <section className="space-y-3">
        <h2 className="text-sm font-semibold text-white">Traefik network interaction graph</h2>
        <div className="h-[420px] rounded-xl border border-slate-800 bg-slate-950">
          {graph.nodes.length === 0 ? (
            <div className="flex h-full items-center justify-center text-sm text-slate-500">No routes yet.</div>
          ) : (
            <ReactFlow nodes={graph.nodes} edges={graph.edges} fitView nodesDraggable={false} nodesConnectable={false}>
              <Background color="#1e293b" gap={18} />
              <Controls className="!bg-slate-900 !border-slate-700" />
            </ReactFlow>
          )}
        </div>
      </section>

      <section>
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-white">Текущие файлы</h2>
          <button
            type="button"
            onClick={() => refresh()}
            className="text-xs text-emerald-400 hover:text-emerald-300"
          >
            Refresh
          </button>
        </div>
        {routes.length === 0 ? (
          <div className="text-sm text-slate-500">Пусто или каталог недоступен.</div>
        ) : (
          <ul className="space-y-4">
            {routes.map((r) => (
              <li key={r.fileName} className="overflow-hidden rounded-xl border border-slate-800 bg-slate-950/60">
                <div className="flex items-center justify-between border-b border-slate-800 px-4 py-2">
                  <span className="font-mono text-xs text-emerald-300">{r.fileName}</span>
                  <button
                    type="button"
                    className="text-xs text-red-400 hover:text-red-300"
                    onClick={async () => {
                      const m = r.fileName.match(/^chronos-route-(.+)\.ya?ml$/i);
                      const base = m?.[1];
                      if (!base || !confirm(`Remove route key ${base}?`)) return;
                      await deleteTraefikRoute(base);
                      refresh();
                    }}
                  >
                    Delete
                  </button>
                </div>
                <div className="border-b border-slate-800 px-4 py-2 text-[11px] text-slate-500">
                  route key:{" "}
                  <span className="font-mono text-slate-300">
                    {r.fileName.replace(/^chronos-route-/, "").replace(/\.ya?ml$/i, "")}
                  </span>
                </div>
                <pre className="max-h-56 overflow-auto p-4 font-mono text-[11px] text-slate-400">{r.yaml}</pre>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
    </ReactFlowProvider>
  );
}

function buildTraefikGraph(routes: { fileName: string; yaml: string }[]): { nodes: Node[]; edges: Edge[] } {
  const nodes: Node[] = [];
  const edges: Edge[] = [];
  const seen = new Set<string>();

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
        width: 190,
        fontSize: 12
      }
    });
  };

  pushNode("traefik", "Traefik entrypoint:web", "#38bdf8", 60, 120);
  routes.forEach((r, i) => {
    const routeKey = r.fileName.replace(/^chronos-route-/, "").replace(/\.ya?ml$/i, "");
    const rule = /rule:\s*"([^"]+)"/.exec(r.yaml)?.[1] ?? "rule ?";
    const routeNodeId = `route:${routeKey}`;
    pushNode(routeNodeId, `${routeKey}\n${rule}`, "#34d399", 360, i * 170 + 40);
    edges.push({ id: `e-traefik-${routeNodeId}`, source: "traefik", target: routeNodeId, label: "routes" });

    const urls = [...r.yaml.matchAll(/url:\s*"([^"]+)"/g)].map((m) => m[1]);
    urls.forEach((u, bi) => {
      const backendId = `backend:${u}`;
      pushNode(backendId, u, "#f59e0b", 700, i * 170 + bi * 65 + 20);
      edges.push({ id: `e-${routeNodeId}-${backendId}-${bi}`, source: routeNodeId, target: backendId, label: "forward" });
    });
  });

  return { nodes, edges };
}
