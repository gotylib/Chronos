import { useState } from "react";
import {
  clusterDeploy,
  clusterPublish,
  fluentPreview,
  validateComposeYaml,
  type FluentPreviewResponse
} from "../api/client";

const fluentStarter = `var cb = new ComposeBuilder()
    .WithProjectName("demo-ui")
    .AddNetwork("front")
    .AddService(s => s
        .WithName("web")
        .UseImage("nginx:alpine")
        .ConnectToNetwork("front"));
return cb.Build();`;

export function SandboxPage() {
  const [tab, setTab] = useState<"yaml" | "fluent" | "deploy">("yaml");
  const [yaml, setYaml] = useState(`version: "3.8"
services:
  web:
    image: nginx:alpine
`);
  const [yamlOut, setYamlOut] = useState<string | null>(null);

  const [fluent, setFluent] = useState(fluentStarter);
  const [fluentRes, setFluentRes] = useState<FluentPreviewResponse | null>(null);

  const [proj, setProj] = useState("demo-ui");
  const [deployYaml, setDeployYaml] = useState("");
  const [manifest, setManifest] = useState("");
  const [deployOut, setDeployOut] = useState<string | null>(null);

  async function runYamlValidate() {
    setYamlOut(null);
    try {
      const j = await validateComposeYaml(yaml);
      setYamlOut(JSON.stringify(j, null, 2));
    } catch (e) {
      setYamlOut(String(e));
    }
  }

  async function runFluent() {
    setFluentRes(null);
    const r = await fluentPreview(fluent);
    setFluentRes(r);
  }

  async function runDeploy(kind: "deploy" | "publish") {
    setDeployOut(null);
    try {
      const body = {
        projectName: proj,
        composeYaml: deployYaml,
        manifestJson: manifest.trim() ? manifest : undefined
      };
      const r = kind === "deploy" ? await clusterDeploy(body) : await clusterPublish(body);
      setDeployOut(typeof r === "string" ? r : JSON.stringify(r, null, 2));
    } catch (e) {
      setDeployOut(String(e));
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-white">Sandbox</h1>
        <p className="mt-1 max-w-3xl text-sm text-slate-400">
          Валидация compose YAML, прогон Fluent API на мастере (Roslyn, только при включённой песочнице), затем деплой в кластер через{" "}
          <code className="text-emerald-400">/cluster/deploy</code> или publish.
        </p>
      </div>

      <div className="flex flex-wrap gap-2 border-b border-slate-800 pb-3">
        {(["yaml", "fluent", "deploy"] as const).map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setTab(t)}
            className={`rounded-lg px-4 py-2 text-sm font-medium ${
              tab === t
                ? "bg-emerald-900/60 text-emerald-100 ring-1 ring-emerald-700"
                : "text-slate-400 hover:bg-slate-800 hover:text-white"
            }`}
          >
            {t === "yaml" ? "Compose YAML" : t === "fluent" ? "Fluent API (C#)" : "Deploy"}
          </button>
        ))}
      </div>

      {tab === "yaml" ? (
        <section className="space-y-3">
          <textarea
            value={yaml}
            onChange={(e) => setYaml(e.target.value)}
            rows={14}
            className="w-full rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-xs text-emerald-100 outline-none focus:border-emerald-700"
          />
          <button
            type="button"
            onClick={() => runYamlValidate()}
            className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-600"
          >
            Validate (Core parser + ValidateAsync)
          </button>
          {yamlOut !== null ? (
            <pre className="overflow-auto rounded-xl border border-slate-800 bg-black/40 p-4 font-mono text-xs text-slate-200">{yamlOut}</pre>
          ) : null}
        </section>
      ) : null}

      {tab === "fluent" ? (
        <section className="space-y-3">
          <p className="text-xs text-slate-500">
            Тело метода — только тело функции: переменные и <code className="text-emerald-400">return cb.Build();</code>. Нужны{" "}
            <code className="text-sky-400">CHRONOS_MASTER_FLUENT_SANDBOX_ENABLED=1</code> или Development.
          </p>
          <textarea
            value={fluent}
            onChange={(e) => setFluent(e.target.value)}
            rows={18}
            className="w-full rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-xs text-emerald-100 outline-none focus:border-emerald-700"
          />
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => runFluent()}
              className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-600"
            >
              Validate &amp; generate YAML preview
            </button>
            <button
              type="button"
              onClick={() => setDeployYaml(fluentRes?.generatedYamlPreview ?? "")}
              disabled={!fluentRes?.generatedYamlPreview}
              className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200 hover:bg-slate-800 disabled:opacity-40"
            >
              Copy YAML preview → Deploy tab
            </button>
          </div>
          {fluentRes ? (
            <div className="space-y-2 rounded-xl border border-slate-800 bg-slate-950/60 p-4">
              {!fluentRes.enabled ? (
                <div className="text-sm text-amber-300">{fluentRes.error}</div>
              ) : fluentRes.success ? (
                <div className="text-sm text-emerald-400">Validation OK</div>
              ) : (
                <div className="text-sm text-red-400">{fluentRes.error}</div>
              )}
              {fluentRes.generatedYamlPreview ? (
                <pre className="max-h-[360px] overflow-auto font-mono text-[11px] text-slate-300">
                  {fluentRes.generatedYamlPreview}
                </pre>
              ) : null}
            </div>
          ) : null}
        </section>
      ) : null}

      {tab === "deploy" ? (
        <section className="grid max-w-4xl gap-4">
          <label className="block text-xs text-slate-500">
            Project name
            <input
              value={proj}
              onChange={(e) => setProj(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 font-mono text-sm text-white"
            />
          </label>
          <label className="block text-xs text-slate-500">
            Compose YAML
            <textarea
              value={deployYaml}
              onChange={(e) => setDeployYaml(e.target.value)}
              rows={14}
              placeholder="Вставьте YAML из вкладки Fluent или YAML…"
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-xs text-emerald-100"
            />
          </label>
          <label className="block text-xs text-slate-500">
            Manifest JSON (опционально, .chronos/manifest.json на агенте)
            <textarea
              value={manifest}
              onChange={(e) => setManifest(e.target.value)}
              rows={6}
              className="mt-1 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-xs text-slate-300"
            />
          </label>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => runDeploy("deploy")}
              className="rounded-lg bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-600"
            >
              Deploy to cluster
            </button>
            <button
              type="button"
              onClick={() => runDeploy("publish")}
              className="rounded-lg border border-emerald-800 px-4 py-2 text-sm text-emerald-200 hover:bg-emerald-950/50"
            >
              Publish (same pipeline)
            </button>
          </div>
          {deployOut !== null ? (
            <pre className="overflow-auto rounded-xl border border-slate-800 bg-black/40 p-4 font-mono text-xs text-slate-200">{deployOut}</pre>
          ) : null}
        </section>
      ) : null}
    </div>
  );
}
