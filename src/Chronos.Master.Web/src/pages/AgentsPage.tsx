import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

import type { AgentInfo } from "../api/client";

export function AgentsPage() {
  const [agents, setAgents] = useState<AgentInfo[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetch("/agents")
      .then((r) => {
        if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
        return r.json();
      })
      .then((data: AgentInfo[]) => {
        if (!cancelled) setAgents(data);
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (error) {
    return (
      <div className="rounded-xl border border-red-900/60 bg-red-950/40 p-4 text-red-200">
        Failed to load agents: {error}. Open the UI under /ui/ with Master on the same origin, or run Vite dev with proxy.
      </div>
    );
  }

  if (!agents) {
    return <p className="text-slate-400">Loading agents…</p>;
  }

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold text-white">Agents</h1>
      <div className="overflow-hidden rounded-xl border border-slate-800 bg-slate-900/50 shadow-xl shadow-black/30">
        <table className="min-w-full text-left text-sm">
          <thead className="border-b border-slate-800 bg-slate-900 font-medium text-slate-400">
            <tr>
              <th className="px-4 py-3">Agent</th>
              <th className="px-4 py-3">Base URL</th>
              <th className="px-4 py-3">Location</th>
              <th className="px-4 py-3">Heartbeat</th>
              <th className="px-4 py-3 text-right">CPU %</th>
              <th className="px-4 py-3 text-right">Mem %</th>
              <th className="px-4 py-3 text-right">Disk %</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800">
            {agents.map((a) => (
              <tr key={a.agentId} className="hover:bg-slate-800/40">
                <td className="px-4 py-3 font-mono text-emerald-300">{a.agentId}</td>
                <td className="px-4 py-3 font-mono text-xs text-slate-300">{a.baseUrl}</td>
                <td className="px-4 py-3">{a.location ?? "—"}</td>
                <td className="px-4 py-3 font-mono text-xs text-slate-400">{a.lastHeartbeatUtc ?? "—"}</td>
                <td className="px-4 py-3 text-right">{a.cpuPercent?.toFixed(1) ?? "—"}</td>
                <td className="px-4 py-3 text-right">{a.memoryPercent?.toFixed(1) ?? "—"}</td>
                <td className="px-4 py-3 text-right">{a.diskPercent?.toFixed(1) ?? "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {agents.length === 0 ? (
          <div className="px-4 py-8 text-center text-slate-500">No agents registered yet.</div>
        ) : null}
      </div>
      <ProjectsHint />
    </div>
  );
}

function ProjectsHint() {
  const [projects, setProjects] = useState<{ projectName: string; agentId: string }[] | null>(null);

  useEffect(() => {
    fetch("/cluster/projects")
      .then((r) => (r.ok ? r.json() : Promise.resolve([])))
      .then(setProjects)
      .catch(() => setProjects([]));
  }, []);

  if (!projects?.length) return null;

  return (
    <section className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
      <h2 className="mb-2 text-lg font-medium text-white">Known projects</h2>
      <ul className="space-y-1 font-mono text-xs text-slate-400">
        {projects.map((p) => (
          <li key={p.projectName}>
            <Link
              className="text-emerald-300 hover:text-emerald-100 hover:underline"
              to={`../projects/${encodeURIComponent(p.projectName)}`}
            >
              {p.projectName}
            </Link>
            <span className="text-slate-600"> → </span>
            {p.agentId}
          </li>
        ))}
      </ul>
    </section>
  );
}
