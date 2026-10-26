/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import preact from "@preact/preset-vite";
import { fileURLToPath } from "node:url";

const here = (file: string) => fileURLToPath(new URL(file, import.meta.url));

export default defineConfig({
  plugins: [preact()],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    rollupOptions: {
      input: {
        index: here("./index.html"),
        dev: here("./dev.html"),
      },
    },
  },
  server: {
    proxy: {
      // The ArcBox server (dotnet run --urls http://0.0.0.0:5080) hosts the SignalR hub.
      "/hub": { target: "http://localhost:5080", ws: true, changeOrigin: true },
    },
  },
  test: {
    environment: "happy-dom",
    include: ["test/**/*.test.{ts,tsx}"],
  },
});
