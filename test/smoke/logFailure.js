// @ts-check

/**
 * Escapes a message for a GitHub workflow command, see
 * https://github.com/actions/toolkit/blob/main/packages/core/src/command.ts
 * @param {string} message
 */
function escape(message) {
  return message.replace(/%/g, "%25").replace(/\r/g, "%0D").replace(/\n/g, "%0A");
}

/**
 * Logs the message, and on GitHub Actions also as an error on the summary page of the run.
 * @param {string} message
 */
exports.logFailure = function logFailure(message) {
  console.error(message);
  if (process.env.GITHUB_ACTIONS) console.log(`::error title=Smoke test::${escape(message)}`);
};
