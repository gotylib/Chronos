export type AgentInfo = {
  agentId: string;
  baseUrl: string;
  location?: string | null;
  capabilitiesJson?: string;
  registeredUtc?: string;
  lastHeartbeatUtc?: string;
  cpuPercent?: number;
  memoryPercent?: number;
  diskPercent?: number;
};

export type ProjectPlacement = {
  projectName: string;
  agentId: string;
  agentUrl: string;
  updatedUtc: string;
};

export type ComposeGraphNode = {
  id: string;
  label: string;
  kind: string;
  subtitle?: string | null;
};

export type ComposeGraphEdge = {
  from: string;
  to: string;
  kind: string;
};

export type ComposeGraphDto = {
  nodes: ComposeGraphNode[];
  edges: ComposeGraphEdge[];
};

export type ProjectFullStatus = {
  projectName: string;
  agentId: string;
  agentUrl: string;
  statusJson: string;
  composeYaml: string;
  diagnosticsJson: string;
  statusOk: boolean;
  composeOk: boolean;
  diagnosticsOk: boolean;
};

export type DeployStatus = {
  deploymentId?: string;
  success: boolean;
  error?: string;
  operationPending?: boolean;
  deploymentInProgress?: boolean;
  message?: string;
  publishedHostPorts?: Array<{
    serviceName: string;
    containerPort: number;
    requestedHostPort: number;
    actualHostPort: number;
  }>;
  containers?: Array<{ name: string; state: string }>;
  diagnostics?: {
    recentTests?: Array<{ service: string; testId: string; success: boolean; message?: string; utcTime: string }>;
    recentJobs?: Array<{ service: string; jobId: string; success: boolean; message?: string; utcTime: string }>;
  };
};

export type VolumeArchiveRecord = {
  id: string;
  volumeName: string;
  projectName: string;
  storedRelativePath: string;
  bytesApprox?: number | null;
  compressMode?: string | null;
  createdUtc: string;
};

export type FluentPreviewResponse = {
  enabled: boolean;
  success: boolean;
  validation?: unknown;
  generatedYamlPreview?: string;
  error?: string;
};

export async function getAgents(): Promise<AgentInfo[]> {
  const r = await fetch("/agents");
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  return r.json();
}

export async function getProjects(): Promise<ProjectPlacement[]> {
  const r = await fetch("/cluster/projects");
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  return r.json();
}

export type ArchivedProjectRow = {
  archiveId: string;
  projectName: string;
  agentId: string;
  agentUrl: string;
  archivedUtc: string;
  purgeAfterUtc: string;
};

export async function listArchivedProjects(): Promise<ArchivedProjectRow[]> {
  const r = await fetch("/cluster/projects/archived");
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  return r.json();
}

export type ArchiveProjectAgentPayload = {
  archiveId: string;
  projectName: string;
  archivedUtc: string;
  purgeAfterUtc: string;
  retentionDays: number;
};

export async function archiveProjectCluster(projectName: string): Promise<ArchiveProjectAgentPayload> {
  const r = await fetch(`/cluster/projects/${encodeURIComponent(projectName)}/archive`, { method: "POST" });
  const text = await r.text();
  if (!r.ok) throw new Error(text || `${r.status}`);
  return JSON.parse(text) as ArchiveProjectAgentPayload;
}

export async function restoreArchivedProject(archiveId: string): Promise<void> {
  const r = await fetch(`/cluster/projects/archived/${encodeURIComponent(archiveId)}/restore`, {
    method: "POST"
  });
  if (!r.ok) throw new Error(await r.text());
}

export async function purgeArchivedProjectNow(archiveId: string): Promise<void> {
  const r = await fetch(`/cluster/projects/archived/${encodeURIComponent(archiveId)}`, { method: "DELETE" });
  if (!r.ok) throw new Error(await r.text());
}

export async function getProjectFullStatus(projectName: string): Promise<ProjectFullStatus> {
  const r = await fetch(`/api/v1/cluster/projects/${encodeURIComponent(projectName)}/full-status`);
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  return r.json();
}

export async function postComposeGraph(composeYaml: string): Promise<ComposeGraphDto> {
  const r = await fetch("/api/v1/compose/graph", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ composeYaml })
  });
  if (!r.ok) {
    const t = await r.text();
    throw new Error(t || `${r.status}`);
  }
  return r.json();
}

export type HaproxyTcpRoute = {
  id: string;
  name: string;
  backendHost: string;
  backendPort: number;
  listenPort: number;
  agentId?: string | null;
  projectName?: string | null;
  createdUtc: string;
};

export type HaproxyTcpRoutesResponse = {
  dynamicDirectory?: string | null;
  masterPublicHost?: string | null;
  routes: HaproxyTcpRoute[];
  generatedCfg?: string | null;
  /** Подсказка с Master (Docker): DNS сервиса агента вместо 127.0.0.1 внутри HAProxy. */
  suggestedBackendHost?: string | null;
  suggestedBackendPort?: number | null;
  listenPortMin?: number | null;
  listenPortMax?: number | null;
};

export async function getHaproxyTcpRoutes(): Promise<HaproxyTcpRoutesResponse> {
  const r = await fetch("/api/v1/haproxy/tcp-routes");
  if (!r.ok) throw new Error(`${r.status}`);
  return r.json();
}

export async function addHaproxyTcpRoute(body: {
  name?: string;
  backendHost: string;
  backendPort: number;
  listenPort?: number | null;
  agentId?: string | null;
  projectName?: string | null;
}): Promise<HaproxyTcpRoute> {
  const r = await fetch("/api/v1/haproxy/tcp-routes", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  const text = await r.text();
  if (!r.ok) {
    let msg = text || `${r.status}`;
    try {
      const j = JSON.parse(text) as { error?: string };
      if (typeof j?.error === "string" && j.error.length > 0) msg = j.error;
    } catch {
      /* not JSON */
    }
    throw new Error(msg);
  }
  return JSON.parse(text) as HaproxyTcpRoute;
}

export async function deleteHaproxyTcpRoute(id: string): Promise<void> {
  const r = await fetch(`/api/v1/haproxy/tcp-routes/${encodeURIComponent(id)}`, { method: "DELETE" });
  if (!r.ok) throw new Error(await r.text());
}

export async function validateComposeYaml(composeYaml: string): Promise<unknown> {
  const r = await fetch("/api/v1/sandbox/validate-compose", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ composeYaml })
  });
  const text = await r.text();
  if (!r.ok) throw new Error(text);
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export async function fluentPreview(code: string): Promise<FluentPreviewResponse> {
  const r = await fetch("/api/v1/sandbox/fluent-preview", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code })
  });
  return r.json();
}

export async function clusterDeploy(body: {
  projectName: string;
  composeYaml: string;
  preferredLocation?: string;
  manifestJson?: string;
}): Promise<unknown> {
  const r = await fetch("/cluster/deploy", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  const text = await r.text();
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

async function readJsonOrText(r: Response): Promise<unknown> {
  const text = await r.text();
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export async function clusterPublish(body: {
  projectName: string;
  composeYaml: string;
  preferredLocation?: string;
  manifestJson?: string;
}): Promise<unknown> {
  const r = await fetch("/cluster/publish", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  const text = await r.text();
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export async function projectStop(projectName: string, removeVolumes: boolean): Promise<DeployStatus | string> {
  const r = await fetch(
    `/cluster/projects/${encodeURIComponent(projectName)}/stop?removeVolumes=${removeVolumes}`,
    { method: "POST" }
  );
  return readJsonOrText(r) as Promise<DeployStatus | string>;
}

export type ClusterActionResult = {
  status: number;
  ok: boolean;
  body: unknown;
};

export type RedeployConflictPayload = {
  code?: string;
  message?: string;
  removedComposeServices?: string[];
  removedNamedVolumes?: string[];
  imageChanges?: Array<{
    serviceName?: string;
    previousImage?: string | null;
    newImage?: string | null;
  }>;
};

function buildComposeFormData(composeYaml?: string, confirm?: boolean): FormData {
  const form = new FormData();
  if (composeYaml !== undefined) form.set("compose", composeYaml);
  if (confirm) form.set("confirm", "true");
  return form;
}

export async function projectRestart(
  projectName: string,
  removeVolumes: boolean,
  options?: { composeYaml?: string; confirm?: boolean }
): Promise<ClusterActionResult> {
  const form = buildComposeFormData(options?.composeYaml, options?.confirm);
  const r = await fetch(
    `/cluster/projects/${encodeURIComponent(projectName)}/restart?removeVolumes=${removeVolumes}`,
    { method: "POST", body: form }
  );
  const body = await readJsonOrText(r);
  return { status: r.status, ok: r.ok, body };
}

export async function projectStart(
  projectName: string,
  options?: { composeYaml?: string; confirm?: boolean }
): Promise<ClusterActionResult> {
  const form = buildComposeFormData(options?.composeYaml, options?.confirm);
  const r = await fetch(`/cluster/projects/${encodeURIComponent(projectName)}/start`, {
    method: "POST",
    body: form
  });
  const body = await readJsonOrText(r);
  return { status: r.status, ok: r.ok, body };
}

export async function saveProjectCompose(
  projectName: string,
  composeYaml: string,
  confirm?: boolean
): Promise<ClusterActionResult> {
  const form = buildComposeFormData(composeYaml, confirm);
  const r = await fetch(`/cluster/projects/${encodeURIComponent(projectName)}/compose`, {
    method: "POST",
    body: form
  });
  const body = await readJsonOrText(r);
  return { status: r.status, ok: r.ok, body };
}

export async function getProjectVolumes(projectName: string): Promise<string[]> {
  const r = await fetch(`/cluster/projects/${encodeURIComponent(projectName)}/volumes`);
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  return r.json();
}

export async function getVolumeArchiveIndex(projectName: string): Promise<VolumeArchiveRecord[]> {
  const r = await fetch(`/cluster/projects/${encodeURIComponent(projectName)}/volume-archive-index`);
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  return r.json();
}

export async function registerVolumeArchive(
  projectName: string,
  body: { volumeName: string; storedRelativePath: string; bytesApprox?: number; compressMode?: string }
): Promise<void> {
  const r = await fetch(`/cluster/projects/${encodeURIComponent(projectName)}/volume-archives/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
  if (!r.ok) throw new Error(await r.text());
}

export function downloadVolumeSnapshot(projectName: string, volumeName: string, compress: "gzip" | "none" = "gzip") {
  const url = `/cluster/projects/${encodeURIComponent(projectName)}/volumes/${encodeURIComponent(volumeName)}/snapshot?compress=${compress}`;
  window.open(url, "_blank", "noopener,noreferrer");
}
