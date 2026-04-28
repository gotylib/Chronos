import { Navigate, Route, Routes } from "react-router-dom";
import { AgentsPage } from "./pages/AgentsPage";
import { DashboardPage } from "./pages/DashboardPage";
import { NetworkMapPage } from "./pages/NetworkMapPage";
import { ProjectDetailPage } from "./pages/ProjectDetailPage";
import { ProjectsPage } from "./pages/ProjectsPage";
import { RoutingPage } from "./pages/RoutingPage";
import { SandboxPage } from "./pages/SandboxPage";
import { ShellLayout } from "./layout/ShellLayout";

export function App() {
  return (
    <Routes>
      <Route element={<ShellLayout />}>
        <Route index element={<Navigate to="dashboard" replace />} />
        <Route path="dashboard" element={<DashboardPage />} />
        <Route path="agents" element={<AgentsPage />} />
        <Route path="projects">
          <Route index element={<ProjectsPage />} />
          <Route path=":name" element={<ProjectDetailPage />} />
        </Route>
        <Route path="network" element={<NetworkMapPage />} />
        <Route path="routing" element={<RoutingPage />} />
        <Route path="sandbox" element={<SandboxPage />} />
      </Route>
    </Routes>
  );
}
