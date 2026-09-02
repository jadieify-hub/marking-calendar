import { defineConfig } from "vitest/config";

export default defineConfig({
  base: "./",
  build: {
    target: "chrome111",
    outDir: "dist",
    emptyOutDir: true,
  },
  test: {
    css: true,
    environment: "jsdom",
    restoreMocks: true,
  },
});
