import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import {
  listArchivedProjects,
  purgeArchivedProjectNow,
  restoreArchivedProject,
  type ArchivedProjectRow
} from "../api/client";

function formatUtc(iso: string) {
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

export function ArchivedProjectsPage() {
  const [rows, setRows] = useState<ArchivedProjectRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  function reload() {
    setError(null);
    listArchivedProjects()
      .then(setRows)
      .catch((e: Error) => setError(e.message));
  }

  useEffect(() => {
    reload();
  }, []);

  async function onRestore(a: ArchivedProjectRow) {
    if (
      !window.confirm(
        `Восстановить проект «${a.projectName}» из архива на агенте? Compose и файлы проекта вернутся в активную папку.`
      )
    )
      return;
    setBusy(`restore:${a.archiveId}`);
    setError(null);
    try {
      await restoreArchivedProject(a.archiveId);
      reload();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(null);
    }
  }

  async function onPurge(a: ArchivedProjectRow) {
    if (
      !window.confirm(
        `Удалить архив «${a.projectName}» (${a.archiveId}) с диска агента без ожидания срока? Это необратимо.`
      )
    )
      return;
    setBusy(`purge:${a.archiveId}`);
    setError(null);
    try {
      await purgeArchivedProjectNow(a.archiveId);
      reload();
    } catch (e) {
      setError(String(e));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <Link className="text-xs text-slate-500 hover:text-emerald-400" to="..">
          ← Active projects
        </Link>
        <h1 className="mt-2 text-2xl font-semibold text-white">Archived projects</h1>
        <p className="mt-1 text-sm text-slate-400">
          Проекты сняты с кластера: каталог compose перенесён на агента в архив. Docker-тома не удалялись при остановке.
          После даты purge запись и каталог удаляются автоматически; до этого можно восстановить или удалить вручную.
        </p>
      </div>

      {error ? (
        <div className="rounded-lg border border-red-900/50 bg-red-950/30 p-3 text-sm text-red-200">{error}</div>
      ) : null}

      {!rows ? (
        <div className="text-slate-500">Loading…</div>
      ) : rows.length === 0 ? (
        <div className="rounded-xl border border-slate-800 bg-slate-950/50 p-6 text-sm text-slate-500">
          Нет архивных проектов.
        </div>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-slate-800">
          <table className="min-w-full divide-y divide-slate-800 text-left text-sm">
            <thead className="bg-slate-900/80 text-xs uppercase tracking-wide text-slate-500">
              <tr>
                <th className="px-4 py-3 font-medium">Project</th>
                <th className="px-4 py-3 font-medium">Archive ID</th>
                <th className="px-4 py-3 font-medium">Agent</th>
                <th className="px-4 py-3 font-medium">Archived</th>
                <th className="px-4 py-3 font-medium">Purge after</th>
                <th className="px-4 py-3 font-medium text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800 text-slate-200">
              {rows.map((a) => (
                <tr key={a.archiveId} className="hover:bg-slate-900/40">
                  <td className="px-4 py-3 font-mono text-xs">{a.projectName}</td>
                  <td className="max-w-[140px] truncate px-4 py-3 font-mono text-xs text-slate-400" title={a.archiveId}>
                    {a.archiveId}
                  </td>
                  <td className="px-4 py-3 text-xs text-slate-400">{a.agentId}</td>
                  <td className="whitespace-nowrap px-4 py-3 text-xs text-slate-400">{formatUtc(a.archivedUtc)}</td>
                  <td className="whitespace-nowrap px-4 py-3 text-xs text-slate-400">{formatUtc(a.purgeAfterUtc)}</td>
                  <td className="whitespace-nowrap px-4 py-3 text-right">
                    <div className="flex flex-wrap justify-end gap-2">
                      <button
                        type="button"
                        disabled={busy !== null}
                        onClick={() => onRestore(a)}
                        className="rounded-lg border border-emerald-900/60 bg-emerald-950/40 px-2 py-1 text-xs font-medium text-emerald-100 hover:bg-emerald-900/40 disabled:opacity-50"
                      >
                        Restore
                      </button>
                      <button
                        type="button"
                        disabled={busy !== null}
                        onClick={() => onPurge(a)}
                        className="rounded-lg border border-red-900/60 bg-red-950/30 px-2 py-1 text-xs font-medium text-red-200 hover:bg-red-900/40 disabled:opacity-50"
                      >
                        Delete now
                      </button>
                    </div>
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
