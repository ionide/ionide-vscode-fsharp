// @ts-check
// Downloads VS Code into .vscode-test and runs suite.js in it, with the extension from release/
// and the C# stub, on the workspace folder. Set VSCODE_VERSION to test another VS Code version.
const path = require("path");
const { downloadAndUnzipVSCode, runTests } = require("@vscode/test-electron");
const { logFailure } = require("./logFailure");

// VS Code only starts suite.js once the extensions that activate on the workspace have activated,
// so a hanging activation never reaches the timeouts in suite.js. This limit covers that: 2 minutes
// for VS Code and those activations, then the limits in suite.js (60 + 30 + 30 + 180 s), so that a
// timeout in suite.js, which says more, always comes first.
const runTimeoutMs = 2 * 60_000 + 300_000;

async function main() {
  const root = path.resolve(__dirname, "..", "..");

  // Set when this runs from an Electron app, it would start VS Code as plain Node.js
  delete process.env.ELECTRON_RUN_AS_NODE;

  let timedOut = false;
  let timer;

  try {
    const vscodeExecutablePath = await downloadAndUnzipVSCode(process.env.VSCODE_VERSION || "stable");
    const started = Date.now();

    timer = setTimeout(() => {
      timedOut = true;
      logFailure(
        `VS Code did not finish the smoke test within ${runTimeoutMs / 60_000} minutes, closing it. ` +
          "Without the \"is active\" line from suite.js above, an extension hung while it activated on the workspace. " +
          "Otherwise the last line from suite.js shows how far it got. If that line is \"FSAC answered\" or a failure, VS Code hung while it closed.",
      );
      // test-electron closes VS Code on the first Ctrl+C, and kills it on the second
      process.emit("SIGINT");
      setTimeout(() => process.emit("SIGINT"), 10_000).unref();
    }, runTimeoutMs);

    await runTests({
      vscodeExecutablePath,
      extensionDevelopmentPath: [path.join(root, "release"), path.join(__dirname, "csharp-stub")],
      extensionTestsPath: path.join(__dirname, "suite.js"),
      extensionTestsEnv: { SMOKE_TEST_STARTED: String(started) },
      // test-electron adds --disable-workspace-trust, --no-sandbox and --disable-updates itself
      launchArgs: [path.join(__dirname, "workspace"), "--disable-extensions"],
    });
    console.log(`VS Code finished the smoke test after ${Math.round((Date.now() - started) / 1000)}s.`);
  } catch (error) {
    // suite.js logs its own failures, this is the exit code of VS Code or a failed download
    console.error("The smoke test failed:", error);
    process.exit(1);
  } finally {
    clearTimeout(timer);
  }

  // test-electron already exits 1 after a Ctrl+C, this is in case that changes
  if (timedOut) process.exit(1);
}

main();
