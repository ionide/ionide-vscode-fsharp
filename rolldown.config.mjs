// @ts-check
import { defineConfig } from "rolldown";

// Bundles the Fable output in out/ into the single CommonJS file VS Code loads, release/fsharp.js.
// build.fsx adds --minify for the production build and --watch for the Watch pipeline.
export default defineConfig({
  input: "./out/fsharp.js",
  platform: "node",
  // provided by the extension host
  external: ["vscode"],
  output: {
    file: "./release/fsharp.js",
    format: "cjs",
    sourcemap: true,
    codeSplitting: false,
  },
});
