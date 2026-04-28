import { useCallback, useMemo, useState } from "react";
import {
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  type Edge,
  type Node,
  MarkerType
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { postComposeGraph, type ComposeGraphDto } from "../api/client";

const kindColor: Record<string, string> = {
  service: "#34d399",
  network: "#38bdf8",
  volume: "#c084fc",
  other: "#94a3b8"
};

function placeNodes(g: ComposeGraphDto): Node[] {
  const byKind = (k: string) => g.nodes.filter((n) => n.kind === k);
  const services = byKind("service");
  const nets = byKind("network");
  const vols = byKind("volume");
  const rest = g.nodes.filter((n) => !["service", "network", "volume"].includes(n.kind));

  const col = 220;
  const row = 140;
  const out: Node[] = [];
  let y = 0;

  const rowPlace = (list: typeof g.nodes, yy: number) => {
    list.forEach((n, i) => {
      out.push({
        id: n.id,
        position: { x: i * col, y: yy },
        data: {
          label: n.label,
          subtitle: n.subtitle ?? "",
          kind: n.kind
        },
        style: {
          borderColor: kindColor[n.kind] ?? kindColor.other,
          background: "#0f172a",
          color: "#e2e8f0",
          fontSize: 12,
          padding: 10,
          borderRadius: 8,
          borderWidth: 2,
          width: 180
        }
      });
    });
  };

  rowPlace(nets, y);
  y += row;
  rowPlace(services, y);
  y += row;
  rowPlace(vols, y);
  y += row;
  rowPlace(rest, y);

  return out;
}

function toEdges(g: ComposeGraphDto): Edge[] {
  return g.edges.map((e, i) => ({
    id: `${e.from}-${e.kind}-${e.to}-${i}`,
    source: e.from,
    target: e.to,
    label: e.kind,
    animated: e.kind === "depends_on",
    markerEnd: { type: MarkerType.ArrowClosed, color: "#64748b" },
    style: { stroke: "#475569", strokeWidth: e.kind === "depends_on" ? 2 : 1 }
  }));
}

export function NetworkMapPage() {
  const [yaml, setYaml] = useState(`version: "3.8"
services:
  web:
    image: nginx:alpine
    depends_on:
      - api
    networks:
      - front
  api:
    image: alpine:3.19
    command: ["sleep","infinity"]
    networks:
      - front
      - back
  db:
    image: postgres:16-alpine
    networks:
      - back
networks:
  front:
  back:
`);
  const [graph, setGraph] = useState<ComposeGraphDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const nodes = useMemo(() => (graph ? placeNodes(graph) : []), [graph]);
  const edges = useMemo(() => (graph ? toEdges(graph) : []), [graph]);

  const run = useCallback(async () => {
    setError(null);
    try {
      const g = await postComposeGraph(yaml);
      setGraph(g);
    } catch (e) {
      setError(String(e));
      setGraph(null);
    }
  }, [yaml]);

  return (
    <ReactFlowProvider>
    <div className="flex min-h-[70vh] flex-col gap-4">
      <div>
        <h1 className="text-2xl font-semibold text-white">Network map</h1>
        <p className="mt-1 max-w-3xl text-sm text-slate-400">
          Строится из compose YAML: сервисы, <code className="text-emerald-400">depends_on</code>, подключение к{" "}
          <code className="text-sky-400">networks</code> и именованным томам.
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-[1fr,minmax(0,1.2fr)]">
        <div className="flex flex-col gap-2">
          <textarea
            value={yaml}
            onChange={(e) => setYaml(e.target.value)}
            rows={16}
            className="min-h-[200px] flex-1 rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-xs text-emerald-100 outline-none ring-emerald-900/30 focus:border-emerald-700 focus:ring"
          />
          <button
            type="button"
            onClick={() => run()}
            className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-600"
          >
            Build graph
          </button>
          {error ? <div className="text-sm text-red-400">{error}</div> : null}
        </div>

        <div className="h-[480px] min-h-[320px] rounded-xl border border-slate-800 bg-slate-950">
          {graph && nodes.length > 0 ? (
            <ReactFlow
              nodes={nodes}
              edges={edges}
              nodesDraggable={false}
              nodesConnectable={false}
              fitView
              className="bg-slate-950"
              minZoom={0.2}
              maxZoom={1.5}
              defaultEdgeOptions={{ zIndex: 1000 }}
            >
              <Background gap={16} color="#1e293b" />
              <MiniMap
                nodeColor={(n) => kindColor[String(n.data?.kind) as string] ?? "#64748b"}
                maskColor="rgb(15 23 42 / 0.85)"
              />
              <Controls className="!bg-slate-900 !border-slate-700" />
            </ReactFlow>
          ) : (
            <div className="flex h-full items-center justify-center text-sm text-slate-500">
              Нажмите «Build graph»
            </div>
          )}
        </div>
      </div>
    </div>
    </ReactFlowProvider>
  );
}
