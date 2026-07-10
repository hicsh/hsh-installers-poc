import { Injectable, NgZone } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../environments/environment';
import { semverLt, bumpVersion } from '../util/semver';

const BASE = environment.agentBaseUrl;
const VERSION_URL = `${BASE}/version`;
const UPDATE_STATUS_URL = `${BASE}/update/status`;
const UPDATE_TRIGGER_URL = `${BASE}/update`;
const HUB_URL = `${BASE}/hub`;

/** Mirrors the agent's UpdatePolicy enum (GET /update/status). */
export type UpdatePolicy = 'auto' | 'on-demand' | 'disabled';

/** Mirrors the agent's UpdateState enum (GET /update/status). */
export type UpdateState =
  | 'idle'
  | 'checking'
  | 'update-available'
  | 'downloading'
  | 'ready'
  | 'applying'
  | 'up-to-date'
  | 'failed'
  | 'disabled'
  | 'unsupported';

/**
 * Self-update status reported by the agent. `supported` is false when the agent
 * can't self-update (dev build without the endpoint, or policy `disabled`) — the
 * UI then falls back to a download link.
 */
export interface UpdateStatus {
  state: UpdateState;
  policy: UpdatePolicy;
  supported: boolean;
  installed: string;
  latest: string | null;
  percent: number | null;
  error: string | null;
}

export type AgentVersionStatus =
  | { status: 'unknown' }
  | { status: 'ok'; installed: string }
  | { status: 'optional-update'; installed: string; latest: string }
  | { status: 'required-update'; installed: string; latest: string };

const UPDATE_TERMINAL_STATES: ReadonlySet<UpdateState> = new Set<UpdateState>([
  'up-to-date',
  'failed',
  'disabled',
  'unsupported',
]);

/**
 * Lets the update flows be demoed without a real newer release: append
 * ?demoUpdate=optional or ?demoUpdate=required to the app URL.
 *   - optional → a newer version "exists" (top banner).
 *   - required → the installed version is below minimum (data hidden, forced update).
 */
function demoLatest(installed: string): { latest: string; required: boolean } | null {
  const demo = new URLSearchParams(window.location.search).get('demoUpdate');
  if (demo === 'optional') return { latest: bumpVersion(installed, 'patch'), required: false };
  if (demo === 'required') return { latest: bumpVersion(installed, 'major'), required: true };
  return null;
}

@Injectable({ providedIn: 'root' })
export class AgentService {
  private connection: signalR.HubConnection;

  readonly agentOnline$ = new BehaviorSubject<boolean>(false);
  readonly randomNumber$ = new BehaviorSubject<number | null>(null);
  readonly versionStatus$ = new BehaviorSubject<AgentVersionStatus>({ status: 'unknown' });
  readonly updateStatus$ = new BehaviorSubject<UpdateStatus | null>(null);

  private updatePollTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(private zone: NgZone) {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .build();
    this.registerHandlers();
    this.connect();
  }

  private registerHandlers(): void {
    // The simulated live stream: a fresh random number every ~2s while connected.
    this.connection.on('RandomNumber', (value: number) => {
      this.zone.run(() => this.randomNumber$.next(value));
    });

    this.connection.onreconnecting(() => {
      this.zone.run(() => this.agentOnline$.next(false));
    });

    this.connection.onreconnected(() => {
      this.zone.run(() => this.agentOnline$.next(true));
      this.checkVersion();
    });

    this.connection.onclose(() => {
      this.zone.run(() => {
        this.agentOnline$.next(false);
        this.randomNumber$.next(null);
        setTimeout(() => this.connect(), 3000);
      });
    });
  }

  private async connect(): Promise<void> {
    try {
      await this.connection.start();
      this.zone.run(() => this.agentOnline$.next(true));
      this.checkVersion();
    } catch {
      setTimeout(() => this.connect(), 3000);
    }
  }

  /** Reads the running agent's version and classifies it against the config + feed. */
  async checkVersion(): Promise<AgentVersionStatus> {
    let installed: string;
    try {
      const res = await fetch(VERSION_URL);
      if (!res.ok) throw new Error(`unexpected status ${res.status}`);
      installed = (await res.json()).version;
    } catch {
      return this.setVersionStatus({ status: 'unknown' });
    }

    // Refresh self-update capability/policy + the agent-reported latest version.
    const update = await this.refreshUpdateStatus();
    return this.setVersionStatus(this.computeVersionStatus(installed, update?.latest ?? null));
  }

  /**
   * Decides what to show. `required-update` (installed below the configured
   * minimum) hides the live data and forces an update; `optional-update` (a
   * newer build exists) is a dismissible nudge.
   */
  private computeVersionStatus(installed: string, reportedLatest: string | null): AgentVersionStatus {
    const demo = demoLatest(installed);
    const latest = demo?.latest ?? reportedLatest;

    if (demo?.required || semverLt(installed, environment.minAgentVersion)) {
      return { status: 'required-update', installed, latest: latest ?? environment.minAgentVersion };
    }
    if (latest && semverLt(installed, latest)) {
      return { status: 'optional-update', installed, latest };
    }
    return { status: 'ok', installed };
  }

  private setVersionStatus(status: AgentVersionStatus): AgentVersionStatus {
    this.zone.run(() => this.versionStatus$.next(status));
    return status;
  }

  /** Fetches the agent's current self-update status (GET /update/status). */
  async refreshUpdateStatus(): Promise<UpdateStatus | null> {
    try {
      const res = await fetch(UPDATE_STATUS_URL);
      if (!res.ok) throw new Error(`unexpected status ${res.status}`);
      const status = (await res.json()) as UpdateStatus;
      this.zone.run(() => this.updateStatus$.next(status));
      return status;
    } catch {
      this.zone.run(() => this.updateStatus$.next(null));
      return null;
    }
  }

  /**
   * Triggers a self-update (POST /update) and polls progress until the agent
   * starts applying it (at which point the process exits and SignalR drops —
   * reconnect + checkVersion() then refreshes once the new version is up).
   */
  async startUpdate(): Promise<void> {
    try {
      const res = await fetch(UPDATE_TRIGGER_URL, { method: 'POST' });
      if (res.ok) {
        const status = (await res.json()) as UpdateStatus;
        this.zone.run(() => this.updateStatus$.next(status));
      }
    } catch {
      return;
    }
    this.pollUpdateStatus();
  }

  private pollUpdateStatus(): void {
    if (this.updatePollTimer) clearTimeout(this.updatePollTimer);
    this.updatePollTimer = setTimeout(async () => {
      const status = await this.refreshUpdateStatus();
      if (status && status.state !== 'applying' && !UPDATE_TERMINAL_STATES.has(status.state)) {
        this.pollUpdateStatus();
      }
    }, 1000);
  }
}
