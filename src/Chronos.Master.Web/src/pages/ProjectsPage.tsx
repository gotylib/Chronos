import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import {
  getProjects,
  projectRestart,
  projectStart,
  projectStop,
  type ProjectPlacement
} from "../api/client";

export function ProjectsPage() {
  const [rows, setRows] = useState<ProjectPlacement[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  useEffect(() => {
    let c = false;
    getProjects()
      .then((r) => {
        if (!c) setRows(r);
      })
      .catch((e: Error) => {
        if (!c) setError(e.message);
      });
    return () => {
      c = true;
    };
  }, []);

  async function doAction(projectName: string, kind: "start" | "stop" | "restart") {
    setBusy(`${kind}:${projectName}`);
    setError(null);
    try {
      if (kind === "start") {
        const r = await projectStart(projectName);
        if (!r.ok)
          throw new Error(
            r.status === 409
              ? "Нужно подтвердить изменение compose — откройте карточку проекта и сохраните YAML или запустите оттуда."
              : typeof r.body === "string"
                ? r.body
                : JSON.stringify(r.body)
          );
      }
      if (kind === "stop") await projectStop(projectName, false);
      if (kind === "restart") {
        const r = await projectRestart(projectName, false);
        if (!r.ok)
          throw new Error(
            r.status === 409
              ? "Нужно подтвердить изменение compose — откройте карточку проекта."
              : typeof r.body === "string"
                ? r.body
                : JSON.stringify(r.body)
          );
      }
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(null);
    }
  }

  if (error) {
    return (
      <div className="rounded-xl border border-red-900/50 bg-red-950/30 p-4 text-red-200">{error}</div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-white">Projects</h1>
        <p className="mt-1 text-sm text-slate-400">
          Размещение проектов на агенте. Откройте карточку для статуса контейнеров, declarative/code тестов и задач из манифеста.
        </p>
        <p className="mt-2">
          <Link className="text-xs font-medium text-emerald-400/90 hover:text-emerald-300" to="archived">
            Archived projects →
          </Link>
        </p>
      </div>

      {!rows ? (
        <div className="text-slate-500">Loading…</div>
      ) : rows.length === 0 ? (
        <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-8 text-center text-slate-400">
          Нет проектов в реестре мастера. Задеплойте через{" "}
          <Link className="text-emerald-400 underline" to="/sandbox">
            Sandbox
          </Link>{" "}
          или API <code className="text-emerald-300">POST /cluster/deploy</code>.
        </div>
      ) : (
        <div className="overflow-hidden rounded-xl border border-slate-800">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-slate-800 bg-slate-900/60 text-xs uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-3 font-medium">Project</th>
                <th className="px-4 py-3 font-medium">Agent</th>
                <th className="px-4 py-3 font-medium">Updated</th>
                <th className="px-4 py-3 font-medium">Control</th>
                <th className="px-4 py-3 font-medium" />
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/80">
              {rows.map((p) => (
                <tr key={p.projectName} className="hover:bg-slate-900/40">
                  <td className="px-4 py-3 font-mono text-emerald-300">{p.projectName}</td>
                  <td className="px-4 py-3 text-slate-300">{p.agentId}</td>
                  <td className="px-4 py-3 text-slate-500">{new Date(p.updatedUtc).toLocaleString()}</td>
                  <td className="px-4 py-3">
                    <div className="flex gap-1">
                      <button
                        type="button"
                        disabled={busy === `start:${p.projectName}`}
                        onClick={() => doAction(p.projectName, "start")}
                        className="rounded bg-sky-900/50 px-2 py-1 text-[11px] text-sky-100 hover:bg-sky-800/60"
                      >
                        Start
                      </button>
                      <button
                        type="button"
                        disabled={busy === `stop:${p.projectName}`}
                        onClick={() => doAction(p.projectName, "stop")}
                        className="rounded bg-amber-900/50 px-2 py-1 text-[11px] text-amber-100 hover:bg-amber-800/60"
                      >
                        Stop
                      </button>
                      <button
                        type="button"
                        disabled={busy === `restart:${p.projectName}`}
                        onClick={() => doAction(p.projectName, "restart")}
                        className="rounded bg-emerald-900/50 px-2 py-1 text-[11px] text-emerald-100 hover:bg-emerald-800/60"
                      >
                        Restart
                      </button>
                    </div>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <Link
                      className="rounded-lg bg-emerald-900/50 px-3 py-1.5 text-xs font-medium text-emerald-200 ring-1 ring-emerald-800 hover:bg-emerald-800/50"
                      to={`/projects/${encodeURIComponent(p.projectName)}`}
                    >
                      Details
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
