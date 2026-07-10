import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AgentService, UpdateStatus } from '../services/agent.service';
import { AGENT_OS_LABELS, detectAgentOS, downloadUrlFor } from '../util/agent-os';

/**
 * Shared update affordance used by both the optional-update banner and the
 * required-update view. When the agent reports it can self-update (Velopack
 * install + policy allows it) this is an in-place "Update now" button with live
 * progress; otherwise it falls back to a platform-matched download link — dev
 * builds without the endpoint, or fleets where updates are managed centrally
 * (policy `disabled`).
 */
@Component({
  selector: 'app-update-action',
  standalone: true,
  template: `
    @if (canSelfUpdate()) {
      @switch (status()!.state) {
        @case ('checking') {
          <div class="progress"><div class="bar indeterminate"></div><span>Checking for the latest version…</span></div>
        }
        @case ('downloading') {
          <div class="progress">
            <div class="bar"><div class="fill" [style.width.%]="status()!.percent ?? 0"></div></div>
            <span>Downloading update… {{ status()!.percent ?? 0 }}%</span>
          </div>
        }
        @case ('ready') {
          <div class="progress"><div class="bar indeterminate"></div><span>Installing update…</span></div>
        }
        @case ('applying') {
          <div class="progress"><div class="bar indeterminate"></div><span>Installing update — the agent will restart automatically…</span></div>
        }
        @case ('failed') {
          <div class="failed">Update failed{{ status()!.error ? ': ' + status()!.error : '' }}.</div>
          <div class="actions">
            <button class="btn" (click)="update()">Try again</button>
            <a class="btn ghost" [href]="primaryDownload" target="_blank" rel="noopener">Download for {{ osLabel }}</a>
          </div>
        }
        @default {
          <button class="btn primary" (click)="update()">Update now</button>
        }
      }
    } @else if (isManaged()) {
      <div class="managed">Updates on this machine are managed by your organization.</div>
    } @else {
      <a class="btn primary" [href]="primaryDownload" target="_blank" rel="noopener">Download for {{ osLabel }}</a>
    }
  `,
  styles: [`
    :host { display: block; }
    .progress { display: flex; flex-direction: column; gap: 6px; }
    .progress span { font-size: 13px; color: #5a6472; }
    .bar { position: relative; height: 6px; border-radius: 4px; background: #e3e7ee; overflow: hidden; }
    .bar .fill { height: 100%; background: #3b6ef5; transition: width .2s ease; }
    .bar.indeterminate::after {
      content: ''; position: absolute; inset: 0; width: 40%; border-radius: 4px;
      background: #3b6ef5; animation: slide 1.1s ease-in-out infinite;
    }
    @keyframes slide { 0% { left: -40%; } 100% { left: 100%; } }
    .actions { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 8px; }
    .failed { font-size: 13px; color: #c62828; }
    .managed { font-size: 13px; color: #5a6472; }
    .btn {
      display: inline-flex; align-items: center; justify-content: center;
      padding: 8px 16px; border-radius: 8px; font-size: 14px; font-weight: 600;
      border: 1px solid #c7cedb; background: #fff; color: #1f2733; cursor: pointer;
      text-decoration: none;
    }
    .btn.primary { background: #3b6ef5; border-color: #3b6ef5; color: #fff; }
    .btn.ghost { background: transparent; }
  `],
})
export class UpdateActionComponent {
  private readonly agent = inject(AgentService);
  readonly status = toSignal(this.agent.updateStatus$, { initialValue: null as UpdateStatus | null });

  private readonly os = detectAgentOS();
  readonly osLabel = AGENT_OS_LABELS[this.os];
  readonly primaryDownload = downloadUrlFor(this.os) ?? downloadUrlFor('windows')!;

  /** True when the running agent reports it can update itself in place. */
  canSelfUpdate(): boolean {
    return this.status()?.supported === true;
  }

  /** Distinguishes "managed externally" (show a note) from "dev build" (show download). */
  isManaged(): boolean {
    return this.status()?.policy === 'disabled';
  }

  update(): void {
    this.agent.startUpdate();
  }
}
