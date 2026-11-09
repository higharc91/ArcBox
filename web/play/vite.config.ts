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
      output: {
        // The renderer already imports this shared core separately. Preserve that boundary
        // instead of shipping one oversized flyover chunk on phones.
        manualChunks(id) {
          if (id.endsWith("/three/build/three.core.js")) return "three-core";
        },
      },
      input: {
        index: here("./index.html"),
        dev: here("./dev.html"),
        title: here("./title.html"),
      },
    },
  },
  server: {
    // The tests read the pack's character art to check the look composes as the asset tools bake it (test/tacticsLook.test.ts).
    fs: { allow: [here("."), here("../../content/tactics/base/characters")] },
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
