import path from "path";
import { cwd, env } from "process";
import { nodeResolve } from "@rollup/plugin-node-resolve";
import commonjs from "@rollup/plugin-commonjs";
import rollupJson from "@rollup/plugin-json";

const resolve = (...paths) => path.join(cwd(), ...paths);
const isProduction = env?.IONIDE_MODE === "production";
const ionideExperimental = env?.IONIDE_EXPERIMENTAL === "true";
const outputPath = ionideExperimental ? "release-exp" : "release";

/**
 * @type {import('rollup').RollupOptions}
 */
const config = {
  input: "out/fsharp.js",
  output: {
    file: resolve(outputPath, "fsharp.js"),
    format: "cjs",
  },
  context: "undefined",
  treeshake: isProduction,
  plugins: [nodeResolve(), commonjs(), rollupJson()],
  external: ["vscode"],
};

export default config;
