import { environment } from '../../environments/environment';

export type AgentOS = 'windows' | 'macArm64' | 'macX64' | 'linux' | 'unknown';

/**
 * Browsers don't expose CPU architecture directly (Safari/Chrome both report
 * "Intel" on Apple Silicon for compatibility). The WebGL renderer string is the
 * standard practical workaround — it names the actual GPU.
 */
function detectMacArch(): 'macArm64' | 'macX64' {
  try {
    const canvas = document.createElement('canvas');
    const gl = (canvas.getContext('webgl') ?? canvas.getContext('experimental-webgl')) as WebGLRenderingContext | null;
    const ext = gl?.getExtension('WEBGL_debug_renderer_info');
    const renderer = ext ? (gl!.getParameter(ext.UNMASKED_RENDERER_WEBGL) as string) : '';
    if (/Apple M\d|Apple GPU/i.test(renderer)) return 'macArm64';
  } catch {
    // WebGL unavailable — fall back to the more common build.
  }
  return 'macX64';
}

export function detectAgentOS(): AgentOS {
  const ua = navigator.userAgent.toLowerCase();
  if (ua.includes('win')) return 'windows';
  if (ua.includes('mac')) return detectMacArch();
  if (ua.includes('linux') || ua.includes('x11')) return 'linux';
  return 'unknown';
}

export const AGENT_OS_LABELS: Record<AgentOS, string> = {
  windows: 'Windows',
  macArm64: 'macOS (Apple Silicon)',
  macX64: 'macOS (Intel)',
  linux: 'Linux',
  unknown: 'your OS',
};

/** Maps each OS to its GitHub "latest release" download URL. */
export function downloadUrlFor(os: AgentOS): string | null {
  const base = environment.releaseBaseUrl;
  const a = environment.downloadAssets;
  switch (os) {
    case 'windows': return `${base}/${a.windows}`;
    case 'macArm64': return `${base}/${a.macArm64}`;
    case 'macX64': return `${base}/${a.macX64}`;
    case 'linux': return `${base}/${a.linux}`;
    default: return null;
  }
}

/** Every platform's download, for the "alternative links" list. */
export const ALL_DOWNLOADS: { os: AgentOS; label: string; url: string }[] =
  (['windows', 'macArm64', 'macX64', 'linux'] as AgentOS[]).map((os) => ({
    os,
    label: AGENT_OS_LABELS[os],
    url: downloadUrlFor(os)!,
  }));
