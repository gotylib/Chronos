import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getAgents, getProjects } from "../api/client";

export function DashboardPage() {
  const [agentsN, setAgentsN] = useState<number | null>(null);
  const [projectsN, setProjectsN] = useState<number | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    let c = false;
    Promise.all([getAgents(), getProjects()])
      .then(([a, p]) => {
        if (!c) {
          setAgentsN(a.length);
          setProjectsN(p.length);
        }
      })
      .catch((e: Error) => {
        if (!c) setErr(e.message);
      });
    return () => {
      c = true;
    };
  }, []);

  if (err) {
    return (
      <div className="rounded-xl border border-red-900/50 bg-red-950/30 p-4 text-red-200">
        {err} — is Master running and CORS same-origin?
      </div>
    );
  }

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight text-white">Chronos</h1>
        <p className="mt-1 text-sm text-slate-400">
          Оркестрация compose-проектов по агентам, диагностика тестов и задач, граф сервисов и edge routing (Traefik).
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricCard title="Agents online" value={agentsN === null ? "…" : String(agentsN)} hint="heartbeat last window" />
        <MetricCard title="Tracked projects" value={projectsN === null ? "…" : String(projectsN)} hint="known placements" />
        <Link
          to="../projects"
          className="rounded-xl border border-emerald-900/40 bg-emerald-950/25 p-5 shadow-lg shadow-black/20 transition hover:border-emerald-700/60"
        >
          <div className="text-xs font-medium uppercase tracking-wide text-emerald-400/90">Workload</div>
          <div className="mt-2 text-lg font-semibold text-white">Projects →</div>
          <p className="mt-2 text-xs text-emerald-200/70">Статус, логика тестов и джобов по агентам</p>
        </Link>
        <Link
          to="../network"
          className="rounded-xl border border-sky-900/40 bg-sky-950/25 p-5 shadow-lg shadow-black/20 transition hover:border-sky-700/60"
        >
          <div className="text-xs font-medium uppercase tracking-wide text-sky-400/90">Topology</div>
          <div className="mt-2 text-lg font-semibold text-white">Network map →</div>
          <p className="mt-2 text-xs text-sky-200/70">Compose services, сети, тома и зависимости</p>
        </Link>
      </div>

      <section className="rounded-xl border border-slate-800 bg-slate-950/40 p-5">
        <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-500">Quick links</h2>
        <ul className="mt-3 grid gap-2 text-sm text-emerald-300 sm:grid-cols-2">
          <li>
            <Link className="hover:text-emerald-100" to="../routing">
              Traefik routing
            </Link>
          </li>
          <li>
            <Link className="hover:text-emerald-100" to="../sandbox">
              Sandbox — YAML, Fluent API, деплой
            </Link>
          </li>
          <li>
            <Link className="hover:text-emerald-100" to="../agents">
              Agents
            </Link>
          </li>
          <li>
            <Link className="hover:text-emerald-100" to="../projects">
              Projects
            </Link>
          </li>
        </ul>
      </section>
    </div>
  );
}

function MetricCard(props: { title: string; value: string; hint: string }) {
  return (
    <div className="rounded-xl border border-slate-800 bg-gradient-to-br from-slate-900/80 to-slate-950 p-5 shadow-inner">
      <div className="text-xs font-medium uppercase tracking-wide text-slate-500">{props.title}</div>
      <div className="mt-3 font-mono text-3xl font-semibold tabular-nums text-white">{props.value}</div>
      <div className="mt-2 text-xs text-slate-500">{props.hint}</div>
    </div>
  );
}
