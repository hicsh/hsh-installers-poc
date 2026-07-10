import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AgentService } from '../services/agent.service';

/**
 * The "happy path" view: the agent is online and its version is acceptable, so
 * we show the live random number streaming over SignalR plus the agent version.
 */
@Component({
  selector: 'app-random-number',
  standalone: true,
  template: `
    <div class="card">
      <span class="label">Live value from the agent</span>
      <div class="number">{{ randomNumber() ?? '—' }}</div>
      <span class="meta">Agent v{{ installed() }} · streaming over SignalR</span>
    </div>
  `,
  styles: [`
    .card {
      display: flex; flex-direction: column; align-items: center; gap: 10px;
      padding: 40px 48px; background: #fff; border: 1px solid #e3e7ee;
      border-radius: 16px; box-shadow: 0 8px 24px rgba(20,30,50,.06);
    }
    .label { font-size: 13px; text-transform: uppercase; letter-spacing: .08em; color: #8a93a3; }
    .number {
      font-size: 64px; font-weight: 700; font-variant-numeric: tabular-nums;
      color: #1f2733; min-width: 4ch; text-align: center;
    }
    .meta { font-size: 13px; color: #5a6472; }
  `],
})
export class RandomNumberComponent {
  private readonly agent = inject(AgentService);
  readonly randomNumber = toSignal(this.agent.randomNumber$, { initialValue: null });

  private readonly version = toSignal(this.agent.versionStatus$, {
    initialValue: { status: 'unknown' as const },
  });

  installed(): string {
    const s = this.version();
    return 'installed' in s ? s.installed : '—';
  }
}
