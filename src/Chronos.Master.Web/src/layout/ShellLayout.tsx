import { useState } from "react";
import { NavLink, Outlet } from "react-router-dom";

const nav =
  "rounded-lg px-3 py-2 text-sm font-medium transition-colors hover:bg-slate-800";
const activeNav = "bg-emerald-900/60 text-emerald-200 ring-1 ring-emerald-700/50";

const links: { to: string; label: string }[] = [
  { to: "dashboard", label: "Dashboard" },
  { to: "agents", label: "Agents" },
  { to: "projects", label: "Projects" },
  { to: "projects/archived", label: "Archived projects" },
  { to: "routing", label: "TCP routing" },
  { to: "sandbox", label: "Sandbox" }
];

export function ShellLayout() {
  const [menuOpen, setMenuOpen] = useState(false);
  return (
    <div className="flex min-h-screen flex-col bg-slate-950">
      <header className="sticky top-0 z-10 border-b border-slate-800/90 bg-slate-900/90 px-4 py-3 backdrop-blur supports-[backdrop-filter]:bg-slate-900/75">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-4">
          <span className="text-xl font-semibold tracking-tight text-slate-100">
            Chronos
          </span>
          <div className="relative">
            <button
              type="button"
              onClick={() => setMenuOpen((v) => !v)}
              className="rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-slate-200 hover:bg-slate-800"
            >
              Menu
            </button>
            {menuOpen ? (
              <nav className="absolute right-0 mt-2 flex w-56 flex-col gap-1.5 rounded-xl border border-slate-700 bg-slate-900/95 p-2 shadow-2xl">
                {links.map((l) => (
                  <NavLink
                    key={l.to}
                    to={l.to}
                    end={l.to === "dashboard" || l.to === "projects"}
                    onClick={() => setMenuOpen(false)}
                    className={({ isActive }) =>
                      `block ${nav} ${isActive ? activeNav : "text-slate-300"}`
                    }
                  >
                    {l.label}
                  </NavLink>
                ))}
              </nav>
            ) : null}
          </div>
        </div>
      </header>
      <main className="mx-auto w-full max-w-7xl flex-1 px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}
