import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const master = "http://localhost:5000";

export default defineConfig({
  base: "/ui/",
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      "^/api/v1": {
        target: master,
        changeOrigin: true
      },
      "/agents": { target: master, changeOrigin: true },
      "/cluster": { target: master, changeOrigin: true }
    }
  }
});
