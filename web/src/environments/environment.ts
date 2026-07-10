/**
 * Build-time configuration for the HSH web client.
 *
 * `minAgentVersion` is the gate the user asked for: if the installed agent is
 * older than this, the app hides the live data and forces a (background) update
 * — see AgentService.computeVersionStatus().
 *
 * Download links resolve against GitHub's "latest release" redirect
 * (`/releases/latest/download/<asset>`), so they always point at the newest
 * published installer without rebuilding the web app. The asset names match
 * what scripts/pack.sh / Velopack produce per channel.
 */
export const environment = {
  production: true,
  agentBaseUrl: 'http://localhost:9740',
  minAgentVersion: '0.1.0',
  releaseBaseUrl: 'https://github.com/hicsh/hsh-installers-poc/releases/latest/download',
  // Canonical installer names. The release workflow normalizes Velopack's output
  // to exactly these before attaching them to the GitHub Release, so these links
  // always resolve regardless of Velopack's internal naming.
  downloadAssets: {
    windows: 'HshAgent-win-x64-Setup.exe',
    macArm64: 'HshAgent-osx-arm64-Setup.pkg',
    macX64: 'HshAgent-osx-x64-Setup.pkg',
    linux: 'HshAgent-linux-x64.AppImage',
  },
};
