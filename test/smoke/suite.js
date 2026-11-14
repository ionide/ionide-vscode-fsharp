// @ts-check
// Runs inside VS Code. Activates Ionide and waits until FSAC answers a hover in workspace/Script.fsx,
// which proves the bundle loads, activation runs and the language server starts.
const path = require("path");
const vscode = require("vscode");
const { logFailure } = require("./logFailure");

const extensionId = "Ionide.Ionide-fsharp";
const activateTimeoutMs = 60_000;
const openTimeoutMs = 30_000;
const hoverTimeoutMs = 180_000;

class TimeoutError extends Error {}

/**
 * Fails with a timeout when promise has not settled after ms. VS Code does not cancel a request
 * that never gets an answer, so without this a hung FSAC would hang the test, and CI with it.
 * @template T
 * @param {PromiseLike<T>} promise
 */
function withTimeout(promise, ms, what) {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new TimeoutError(`Timed out after ${Math.round(ms / 1000)}s waiting for ${what}.`)), ms);
  });
  return Promise.race([Promise.resolve(promise), timeout]).finally(() => clearTimeout(timer));
}

/** Calls probe every second until it returns a value, or fails after timeoutMs. */
async function waitFor(what, timeoutMs, probe) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  let hung = false;

  while (Date.now() < deadline) {
    try {
      const value = await withTimeout(probe(), Math.max(deadline - Date.now(), 1), what);
      if (value !== undefined) return value;
    } catch (error) {
      // only the last probe can time out, it gets the time that is left
      if (error instanceof TimeoutError) hung = true;
      else lastError = error;
    }

    await new Promise((resolve) => setTimeout(resolve, 1000));
  }

  const detail = lastError ? ` Last error: ${lastError instanceof Error ? lastError.message : lastError}` : "";
  const hungNote = hung ? " The last request got no answer before the deadline." : "";
  throw new TimeoutError(`Timed out after ${Math.round(timeoutMs / 1000)}s waiting for ${what}.${detail}${hungNote}`);
}

/** @param {vscode.Hover[] | undefined} hovers */
function hoverText(hovers) {
  return (hovers || [])
    .flatMap((hover) => hover.contents)
    .map((content) => (typeof content === "string" ? content : content.value))
    .join("\n");
}

/** @param {number} since */
function seconds(since) {
  return `${Math.round((Date.now() - since) / 1000)}s`;
}

async function smokeTest() {
  const extension = vscode.extensions.getExtension(extensionId);
  if (!extension) throw new Error(`${extensionId} is not loaded.`);

  // Ionide usually activated on the workspace before this runs, and runTest.js limits that. This
  // covers a change in its activation events: activate waits on an error message without a C# extension.
  await withTimeout(extension.activate(), activateTimeoutMs, `${extensionId} to activate`);
  const launched = Number(process.env.SMOKE_TEST_STARTED);
  console.log(`${extensionId} is active${launched ? `, ${seconds(launched)} after VS Code started` : ""}.`);

  const script = vscode.Uri.file(path.join(__dirname, "workspace", "Script.fsx"));
  const openStarted = Date.now();
  const document = await withTimeout(vscode.workspace.openTextDocument(script), openTimeoutMs, "Script.fsx to open");
  await withTimeout(vscode.window.showTextDocument(document), openTimeoutMs, "Script.fsx to show");
  console.log(`Script.fsx is open after ${seconds(openStarted)}, waiting for a hover from FSAC.`);

  // the `answer` in `let answer = 42`
  const position = new vscode.Position(0, 6);
  const hoverStarted = Date.now();

  const text = await waitFor("a hover from FSAC", hoverTimeoutMs, async () => {
    /** @type {vscode.Hover[] | undefined} */
    const hovers = await vscode.commands.executeCommand("vscode.executeHoverProvider", script, position);
    const text = hoverText(hovers);
    if (text.includes("val answer: int")) return text;
    // FSAC answered, but not with the expected text: shows up as the last error when time runs out
    if (text) throw new Error(`FSAC answered the hover without "val answer: int":\n${text}`);
    return undefined;
  });

  console.log(`FSAC answered the hover after ${seconds(hoverStarted)}:\n${text}`);
}

exports.run = async function run() {
  try {
    await smokeTest();
  } catch (error) {
    logFailure(`${error instanceof Error ? error.message : error}`);
    throw error;
  }
};
