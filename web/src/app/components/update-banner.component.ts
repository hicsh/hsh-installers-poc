import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AgentService, AgentVersionStatus } from '../services/agent.service';
import { UpdateActionComponent } from './update-action.component';

/**
 * Slim, dismissible heads-up shown on top when a newer agent build exists but
 * isn't required. Unlike the required-update view this never hides the data —
 * it's a nudge, not a gate.
 */
@Component({
  selector: 'app-update-banner',
  standalone: true,
  imports: [UpdateActionComponent],
  template: `
    @if (optionalUpdate(); as update) {
      @if (dismissedFor !== update.latest) {
        <div class="banner">
          <span class="msg">Agent v{{ update.latest }} is available — you're running v{{ update.installed }}.</span>
          <app-update-action />
          <button class="close" (click)="dismiss(update.latest)" aria-label="Dismiss">✕</button>
        </div>
      }
    }
  `,
  styles: [`
    .banner {
      display: flex; align-items: center; gap: 14px;
      padding: 10px 20px; background: #fff8e1; border-bottom: 1px solid #ffe082;
      font-size: 14px; color: #6d4c00;
    }
    .msg { flex: 1; }
    .close {
      flex-shrink: 0; border: none; background: transparent; color: #6d4c00;
      font-size: 15px; cursor: pointer; line-height: 1;
    }
  `],
})
export class UpdateBannerComponent {
  private readonly agent = inject(AgentService);
  readonly versionStatus = toSignal(this.agent.versionStatus$, {
    initialValue: { status: 'unknown' } as AgentVersionStatus,
  });
  dismissedFor: string | null = null;

  optionalUpdate() {
    const s = this.versionStatus();
    return s.status === 'optional-update' ? s : null;
  }

  dismiss(version: string) {
    this.dismissedFor = version;
  }
}
