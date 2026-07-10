/**
 * Development-build configuration for the HSH web client.
 *
 * Angular's `fileReplacements` swaps environment.ts → this file for the
 * `development` configuration, so this file must be standalone: importing from
 * './environment' would resolve back to this same file (the replacement applies
 * to the import path too), causing a circular self-reference (TS7022).
 *
 * Keep this in sync with environment.ts — only `production` differs.
 */
export const environment = {
  production: false,
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
