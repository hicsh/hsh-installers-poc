import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AgentService, AgentVersionStatus } from './services/agent.service';
import { UpdateBannerComponent } from './components/update-banner.component';
import { RandomNumberComponent } from './components/random-number.component';
import { AgentMissingComponent } from './components/agent-missing.component';
import { RequiredUpdateComponent } from './components/required-update.component';

type View =
  | { kind: 'missing' }
  | { kind: 'required'; installed: string; required: string }
  | { kind: 'ok' };

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [UpdateBannerComponent, RandomNumberComponent, AgentMissingComponent, RequiredUpdateComponent],
  template: `
    <app-update-banner />
    <header class="topbar"><span class="brand">HSH Agent</span></header>
    <main>
      @switch (view().kind) {
        @case ('missing') { <app-agent-missing /> }
        @case ('required') { <app-required-update [installed]="requiredView().installed" [required]="requiredView().required" /> }
        @default { <app-random-number /> }
      }
    </main>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; background: #f4f6fa; }
    .topbar { padding: 16px 24px; }
    .brand { font-weight: 700; color: #1f2733; letter-spacing: .02em; }
    main { display: flex; justify-content: center; align-items: flex-start; padding: 48px 24px; }
  `],
})
export class App {
  private readonly agent = inject(AgentService);

  private readonly online = toSignal(this.agent.agentOnline$, { initialValue: false });
  private readonly version = toSignal(this.agent.versionStatus$, {
    initialValue: { status: 'unknown' } as AgentVersionStatus,
  });

  readonly view = computed<View>(() => {
    if (!this.online()) return { kind: 'missing' };
    const v = this.version();
    if (v.status === 'required-update') return { kind: 'required', installed: v.installed, required: v.latest };
    return { kind: 'ok' };
  });

  // Convenience accessor so the template can read the required-update fields with types.
  readonly requiredView = computed(() => {
    const v = this.view();
    return v.kind === 'required' ? v : { installed: '', required: '' };
  });
}
