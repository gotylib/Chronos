import { useCallback, useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  archiveProjectCluster,
  downloadVolumeSnapshot,
  getProjectVolumes,
  getProjectFullStatus,
  getVolumeArchiveIndex,
  projectStart,
  projectRestart,
  projectStop,
  registerVolumeArchive,
  saveProjectCompose,
  type ClusterActionResult,
  type DeployStatus,
  type ProjectFullStatus,
  type RedeployConflictPayload,
  type VolumeArchiveRecord
} from "../api/client";

type RunRow = {
  service: string;
  id: string;
  success: boolean;
  message?: string;
  time: string;
  kind: "test" | "job";
};

export function ProjectDetailPage() {
  const navigate = useNavigate();
  const { name } = useParams<{ name: string }>();
  const projectName = name ? decodeURIComponent(name) : "";

  const [data, setData] = useState<ProjectFullStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [actionResult, setActionResult] = useState<DeployStatus | string | null>(null);
  const [volumes, setVolumes] = useState<string[]>([]);
  const [archives, setArchives] = useState<VolumeArchiveRecord[]>([]);
  const [archivePath, setArchivePath] = useState("");
  const [archiveVolume, setArchiveVolume] = useState("");
  const [composeDraft, setComposeDraft] = useState("");
  const [composeTouched, setComposeTouched] = useState(false);

  const load = useCallback(() => {
    if (!projectName) return;
    setError(null);
    getProjectFullStatus(projectName)
      .then(setData)
      .catch((e: Error) => setError(e.message));
  }, [projectName]);

  const loadVolumes = useCallback(() => {
    if (!projectName) return;
    getProjectVolumes(projectName).then(setVolumes).catch((e: Error) => setError(e.message));
    getVolumeArchiveIndex(projectName).then(setArchives).catch((e: Error) => setError(e.message));
  }, [projectName]);

  useEffect(() => {
    load();
    loadVolumes();
    const id = setInterval(load, 15_000);
    const id2 = setInterval(loadVolumes, 20_000);
    return () => {
      clearInterval(id);
      clearInterval(id2);
    };
  }, [load, loadVolumes]);

  useEffect(() => {
    setComposeTouched(false);
    setComposeDraft("");
  }, [projectName]);

  useEffect(() => {
    if (!data?.composeOk || composeTouched) return;
    setComposeDraft(data.composeYaml);
  }, [data?.composeYaml, data?.composeOk, composeTouched]);

  async function runWithRedeployConfirm(
    action: (confirm: boolean) => Promise<ClusterActionResult>
  ): Promise<ClusterActionResult> {
    const first = await action(false);
    if (first.status !== 409) return first;
    const p = first.body as RedeployConflictPayload;
    if (!window.confirm(formatRedeployConflict(p))) return first;
    return action(true);
  }

  const runs: RunRow[] = [];
  if (data?.diagnosticsOk && data.diagnosticsJson) {
    try {
      const d = JSON.parse(data.diagnosticsJson) as {
        recentTests?: Array<{
          service: string;
          testId: string;
          success: boolean;
          message?: string;
          utcTime: string;
        }>;
        recentJobs?: Array<{
          service: string;
          jobId: string;
          success: boolean;
          message?: string;
          utcTime: string;
        }>;
      };
      for (const t of d.recentTests ?? []) {
        runs.push({
          service: t.service,
          id: t.testId,
          success: t.success,
          message: t.message,
          time: t.utcTime,
          kind: "test"
        });
      }
      for (const j of d.recentJobs ?? []) {
        runs.push({
          service: j.service,
          id: j.jobId,
          success: j.success,
          message: j.message,
          time: j.utcTime,
          kind: "job"
        });
      }
      runs.sort((a, b) => (a.time < b.time ? 1 : -1));
    } catch {
      /* raw JSON below */
    }
  }

  async function doStart() {
    setBusy("start");
    setActionResult(null);
    setError(null);
    try {
      const res = await runWithRedeployConfirm((confirm) =>
        projectStart(projectName, { composeYaml: composeDraft, confirm })
      );
      if (res.status === 409) {
        setActionResult("Изменение compose отменено.");
        return;
      }
      if (!res.ok) {
        setError(clusterActionErrorText(res));
        return;
      }
      setActionResult(res.body as DeployStatus);
      await load();
      await loadVolumes();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(null);
    }
  }

  async function doStop(rv: boolean) {
    setBusy("stop");
    setActionResult(null);
    try {
      const res = await projectStop(projectName, rv);
      setActionResult(res);
      await load();
      await loadVolumes();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(null);
    }
  }

  async function doRestart(rv: boolean) {
    setBusy("restart");
    setActionResult(null);
    setError(null);
    try {
      const res = await runWithRedeployConfirm((confirm) =>
        projectRestart(projectName, rv, { composeYaml: composeDraft, confirm })
      );
      if (res.status === 409) {
        setActionResult("Изменение compose отменено.");
        return;
      }
      if (!res.ok) {
        setError(clusterActionErrorText(res));
        return;
      }
      setActionResult(res.body as DeployStatus);
      await load();
      await loadVolumes();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(null);
    }
  }

  async function doArchiveProject() {
    const msg =
      `Архивировать проект «${projectName}»?\n\n` +
      `Будет выполнен compose down без удаления именованных Docker-томов; каталог проекта на агенте уйдёт в архив примерно на 7 дней (можно восстановить или дождаться автоудаления).\n` +
      `Запись о размещении в Master будет удалена до восстановления.`;
    if (!window.confirm(msg)) return;
    setBusy("archive");
    setActionResult(null);
    setError(null);
    try {
      const payload = await archiveProjectCluster(projectName);
      setActionResult(
        `Проект заархивирован. Archive ID: ${payload.archiveId}. Удаление каталога с диска после ${payload.purgeAfterUtc}.`
      );
      navigate("../archived");
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(null);
    }
  }

  async function doSaveCompose() {
    setBusy("save-compose");
    setActionResult(null);
    setError(null);
    try {
      const res = await runWithRedeployConfirm((confirm) =>
        saveProjectCompose(projectName, composeDraft, confirm)
      );
      if (res.status === 409) {
        setActionResult("Сохранение compose отменено.");
        return;
      }
      if (!res.ok) {
        setError(clusterActionErrorText(res));
        return;
      }
      setComposeTouched(false);
      setActionResult("Compose сохранён на агенте.");
      await load();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(null);
    }
  }

  if (!projectName) {
    return <div className="text-red-300">Missing project name.</div>;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link className="text-xs text-slate-500 hover:text-emerald-400" to="..">
            ← Projects
          </Link>
          <h1 className="mt-2 font-mono text-2xl font-semibold text-white">{projectName}</h1>
          {data ? (
            <p className="mt-1 text-xs text-slate-500">
              Agent <span className="text-slate-400">{data.agentId}</span> ·{" "}
              <span className="break-all text-slate-600">{data.agentUrl}</span>
            </p>
          ) : null}
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            disabled={busy !== null}
            onClick={() => doStart()}
            className="rounded-lg border border-sky-900/60 bg-sky-950/40 px-3 py-2 text-xs font-medium text-sky-100 hover:bg-sky-900/40 disabled:opacity-50"
          >
            Start
          </button>
          <button
            type="button"
            disabled={busy !== null}
            onClick={() => doStop(false)}
            className="rounded-lg border border-amber-900/60 bg-amber-950/40 px-3 py-2 text-xs font-medium text-amber-100 hover:bg-amber-900/40 disabled:opacity-50"
          >
            Stop
          </button>
          <button
            type="button"
            disabled={busy !== null}
            onClick={() => doStop(true)}
            className="rounded-lg border border-red-900/60 bg-red-950/30 px-3 py-2 text-xs font-medium text-red-200 hover:bg-red-900/40 disabled:opacity-50"
          >
            Stop + volumes
          </button>
          <button
            type="button"
            disabled={busy !== null}
            onClick={() => doRestart(false)}
            className="rounded-lg border border-emerald-900/60 bg-emerald-950/40 px-3 py-2 text-xs font-medium text-emerald-100 hover:bg-emerald-900/40 disabled:opacity-50"
          >
            Restart
          </button>
          <button
            type="button"
            onClick={() => load()}
            className="rounded-lg border border-slate-700 px-3 py-2 text-xs text-slate-300 hover:bg-slate-800"
          >
            Refresh
          </button>
          <button
            type="button"
            disabled={busy !== null}
            onClick={() => void doArchiveProject()}
            className="rounded-lg border border-rose-900/60 bg-rose-950/35 px-3 py-2 text-xs font-medium text-rose-100 hover:bg-rose-900/35 disabled:opacity-50"
          >
            Archive…
          </button>
        </div>
      </div>

      {error ? (
        <div className="rounded-lg border border-red-900/50 bg-red-950/30 p-3 text-sm text-red-200">{error}</div>
      ) : null}
      {actionResult ? (
        <div className="rounded-lg border border-slate-700 bg-slate-900/60 p-3 text-xs text-slate-300">
          {typeof actionResult === "string"
            ? actionResult
            : actionResult.error
              ? `Action error: ${actionResult.error}`
              : actionResult.operationPending || actionResult.deploymentInProgress
                ? "Action accepted: operation pending, polling status..."
                : actionResult.success
                  ? "Action completed successfully."
                  : "Action finished with non-success result."}
        </div>
      ) : null}

      {!data ? (
        <div className="text-slate-500">Loading…</div>
      ) : (
        <>
          <section className="rounded-xl border border-slate-800 bg-slate-950/50">
            <header className="border-b border-slate-800 px-4 py-3">
              <h2 className="text-sm font-semibold text-white">Tests & jobs (manifest)</h2>
              <p className="text-xs text-slate-500">
                Последние записи из диагностики агента (.chronos + scheduler).
              </p>
            </header>
            {runs.length === 0 ? (
              <div className="p-4 text-sm text-slate-500">Нет записей или формат диагностики другой.</div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead className="border-b border-slate-800 text-slate-500">
                    <tr>
                      <th className="px-4 py-2">Kind</th>
                      <th className="px-4 py-2">Service</th>
                      <th className="px-4 py-2">Id</th>
                      <th className="px-4 py-2">Result</th>
                      <th className="px-4 py-2">Time</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/80 font-mono">
                    {runs.slice(0, 80).map((r, i) => (
                      <tr key={`${r.kind}-${r.id}-${i}`}>
                        <td className="px-4 py-2 text-slate-400">{r.kind}</td>
                        <td className="px-4 py-2 text-emerald-300">{r.service}</td>
                        <td className="px-4 py-2">{r.id}</td>
                        <td className={`px-4 py-2 ${r.success ? "text-emerald-400" : "text-red-400"}`}>
                          {r.success ? "ok" : r.message ?? "fail"}
                        </td>
                        <td className="px-4 py-2 text-slate-500">{r.time}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="rounded-xl border border-slate-800 bg-slate-950/50">
            <header className="border-b border-slate-800 px-4 py-3">
              <h2 className="text-sm font-semibold text-white">Volumes & consistent data</h2>
              <p className="text-xs text-slate-500">
                Автоматическое расписание копий сейчас не настроено: копии создаются вручную кнопкой.
              </p>
            </header>
            <div className="space-y-4 p-4">
              <div className="grid gap-3 sm:grid-cols-2">
                <Stat k="Volumes" v={String(volumes.length)} />
                <Stat k="Known copies" v={String(archives.length)} />
              </div>
              {volumes.length === 0 ? (
                <div className="text-xs text-slate-500">No project volumes detected.</div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs">
                    <thead className="border-b border-slate-800 text-slate-500">
                      <tr>
                        <th className="px-2 py-1">Volume</th>
                        <th className="px-2 py-1">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-800/80">
                      {volumes.map((v) => (
                        <tr key={v}>
                          <td className="px-2 py-1 font-mono text-emerald-300">{v}</td>
                          <td className="px-2 py-1">
                            <button
                              type="button"
                              onClick={() => downloadVolumeSnapshot(projectName, v, "gzip")}
                              className="rounded bg-sky-900/50 px-2 py-1 text-[11px] text-sky-100 hover:bg-sky-800/60"
                            >
                              Make copy now (.tar.gz)
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <div className="rounded-lg border border-slate-800 bg-slate-900/40 p-3">
                <p className="mb-2 text-xs text-slate-400">Register existing copy path in archive index</p>
                <div className="grid gap-2 sm:grid-cols-3">
                  <input
                    value={archiveVolume}
                    onChange={(e) => setArchiveVolume(e.target.value)}
                    placeholder="volume name"
                    className="rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs text-slate-200"
                  />
                  <input
                    value={archivePath}
                    onChange={(e) => setArchivePath(e.target.value)}
                    placeholder="stored path (e.g. backups/demo-ui_web_20260428.tar.gz)"
                    className="rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs text-slate-200"
                  />
                  <button
                    type="button"
                    onClick={async () => {
                      if (!archiveVolume.trim() || !archivePath.trim()) return;
                      await registerVolumeArchive(projectName, {
                        volumeName: archiveVolume.trim(),
                        storedRelativePath: archivePath.trim(),
                        compressMode: "gzip"
                      });
                      setArchivePath("");
                      await loadVolumes();
                    }}
                    className="rounded bg-emerald-700 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-600"
                  >
                    Register copy path
                  </button>
                </div>
              </div>

              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead className="border-b border-slate-800 text-slate-500">
                    <tr>
                      <th className="px-2 py-1">Volume</th>
                      <th className="px-2 py-1">Stored path</th>
                      <th className="px-2 py-1">Created</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/80">
                    {archives.length === 0 ? (
                      <tr>
                        <td className="px-2 py-2 text-slate-500" colSpan={3}>
                          No registered copies yet.
                        </td>
                      </tr>
                    ) : (
                      archives.map((a) => (
                        <tr key={a.id}>
                          <td className="px-2 py-1 font-mono text-emerald-300">{a.volumeName}</td>
                          <td className="px-2 py-1 font-mono text-slate-300">{a.storedRelativePath}</td>
                          <td className="px-2 py-1 text-slate-500">{new Date(a.createdUtc).toLocaleString()}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </section>

          <section className="rounded-xl border border-slate-800 bg-slate-950/50">
            <header className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-800 px-4 py-3">
              <div>
                <h2 className="text-sm font-semibold text-white">Compose file</h2>
                <p className="text-xs text-slate-500">
                  Редактируйте YAML здесь; Start/Restart отправляют этот текст на агент. При удалении сервисов или томов
                  система запросит подтверждение.
                </p>
              </div>
              <button
                type="button"
                disabled={busy !== null || !data.composeOk}
                onClick={() => doSaveCompose()}
                className="shrink-0 rounded-lg border border-emerald-900/60 bg-emerald-950/40 px-3 py-2 text-xs font-medium text-emerald-100 hover:bg-emerald-900/40 disabled:opacity-50"
              >
                Сохранить на агенте
              </button>
            </header>
            {!data.composeOk ? (
              <div className="p-4 font-mono text-[11px] text-red-300">
                Compose недоступен: {data.composeYaml || "unknown error"}
              </div>
            ) : (
              <textarea
                value={composeDraft}
                onChange={(e) => {
                  setComposeTouched(true);
                  setComposeDraft(e.target.value);
                }}
                spellCheck={false}
                className="min-h-[320px] w-full resize-y bg-slate-950/80 p-4 font-mono text-[11px] leading-relaxed text-emerald-100 outline-none ring-0 focus:bg-slate-950"
              />
            )}
          </section>

          <section className="rounded-xl border border-slate-800 bg-slate-950/50">
            <header className="border-b border-slate-800 px-4 py-3">
              <h2 className="text-sm font-semibold text-white">Deploy / compose status (agent)</h2>
              <p className="text-xs text-slate-500">Структурированное состояние контейнеров и deployment pipeline.</p>
            </header>
            <StatusPanel ok={data.statusOk} raw={data.statusJson} />
          </section>

          <section className="rounded-xl border border-slate-800 bg-slate-950/50">
            <header className="border-b border-slate-800 px-4 py-3">
              <h2 className="text-sm font-semibold text-white">Diagnostics snapshot</h2>
            </header>
            <DiagnosticsPanel raw={data.diagnosticsJson} />
          </section>
        </>
      )}
    </div>
  );
}

function StatusPanel(props: { ok: boolean; raw: string }) {
  if (!props.ok) return <div className="p-4 text-sm text-red-300">{props.raw}</div>;
  try {
    const s = JSON.parse(props.raw) as DeployStatus;
    return (
      <div className="space-y-4 p-4">
        <div className="grid gap-3 sm:grid-cols-4">
          <Stat k="Success" v={s.success ? "yes" : "no"} />
          <Stat k="Pending" v={s.operationPending ? "yes" : "no"} />
          <Stat k="In progress" v={s.deploymentInProgress ? "yes" : "no"} />
          <Stat k="Deployment id" v={s.deploymentId ?? "—"} />
        </div>
        {s.publishedHostPorts?.length ? (
          <div className="overflow-x-auto">
            <div className="mb-1 text-[11px] uppercase tracking-wide text-slate-500">Published host ports (agent)</div>
            <table className="w-full text-left text-xs">
              <thead className="border-b border-slate-800 text-slate-500">
                <tr>
                  <th className="px-2 py-1">Service</th>
                  <th className="px-2 py-1">Container port</th>
                  <th className="px-2 py-1">Requested host</th>
                  <th className="px-2 py-1">Actual host</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/80">
                {s.publishedHostPorts.map((p, i) => (
                  <tr key={`${p.serviceName}-${p.containerPort}-${i}`}>
                    <td className="px-2 py-1 font-mono text-amber-200/90">{p.serviceName}</td>
                    <td className="px-2 py-1 text-slate-300">{p.containerPort}</td>
                    <td className="px-2 py-1 text-slate-400">{p.requestedHostPort}</td>
                    <td className="px-2 py-1 font-mono text-emerald-300">{p.actualHostPort}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
        {s.containers?.length ? (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="border-b border-slate-800 text-slate-500">
                <tr>
                  <th className="px-2 py-1">Container</th>
                  <th className="px-2 py-1">State</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/80">
                {s.containers.map((c) => (
                  <tr key={c.name}>
                    <td className="px-2 py-1 font-mono text-emerald-300">{c.name}</td>
                    <td className="px-2 py-1 text-slate-300">{c.state}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : !s.publishedHostPorts?.length ? (
          <div className="text-xs text-slate-500">No container state returned.</div>
        ) : null}
      </div>
    );
  } catch {
    return <pre className="max-h-72 overflow-auto p-4 text-xs text-slate-400">{props.raw}</pre>;
  }
}

function DiagnosticsPanel(props: { raw: string }) {
  try {
    const d = JSON.parse(props.raw) as { recentTests?: unknown[]; recentJobs?: unknown[] };
    return (
      <div className="grid gap-3 p-4 sm:grid-cols-2">
        <Stat k="Recent tests" v={String(d.recentTests?.length ?? 0)} />
        <Stat k="Recent jobs" v={String(d.recentJobs?.length ?? 0)} />
      </div>
    );
  } catch {
    return <div className="p-4 text-xs text-slate-500">No diagnostics payload.</div>;
  }
}

function Stat(props: { k: string; v: string }) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/50 p-3">
      <div className="text-[11px] uppercase tracking-wide text-slate-500">{props.k}</div>
      <div className="mt-1 font-mono text-sm text-slate-200">{props.v}</div>
    </div>
  );
}

function formatRedeployConflict(p: RedeployConflictPayload): string {
  const lines: string[] = [p.message ?? "Изменение compose требует подтверждения."];
  if (p.removedComposeServices?.length)
    lines.push(`Из compose будут убраны сервисы: ${p.removedComposeServices.join(", ")}`);
  if (p.removedNamedVolumes?.length)
    lines.push(`Именованные тома пропадут из файла: ${p.removedNamedVolumes.join(", ")}`);
  if (p.imageChanges?.length) {
    const rows = p.imageChanges.map(
      (c) =>
        ` • ${c.serviceName ?? "?"}: ${c.previousImage ?? "—"} → ${c.newImage ?? "—"}`
    );
    lines.push(`Образы изменятся:\n${rows.join("\n")}`);
  }
  lines.push("\nПродолжить?");
  return lines.join("\n\n");
}

function clusterActionErrorText(res: ClusterActionResult): string {
  const b = res.body;
  if (typeof b === "string") return b;
  if (b && typeof b === "object") {
    const o = b as { error?: unknown; message?: unknown };
    if (typeof o.error === "string") return o.error;
    if (typeof o.message === "string") return o.message;
  }
  return `${res.status}: ${JSON.stringify(b)}`;
}
