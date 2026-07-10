import { Component, Input } from '@angular/core';
import { UpdateActionComponent } from './update-action.component';

/**
 * Shown when the agent is online but its version is below the configured
 * minimum (environment.minAgentVersion). The live data stays hidden and the
 * user is pushed to update — via the same in-place self-update used by the
 * banner (it runs in the background and the agent restarts itself), falling
 * back to a download link when self-update isn't available.
 */
@Component({
  selector: 'app-required-update',
  standalone: true,
  imports: [UpdateActionComponent],
  template: `
    <div class="card">
      <h1>Update required</h1>
      <div class="versions">
        <span class="chip installed">v{{ installed }} installed</span>
        <span class="arrow">→</span>
        <span class="chip required">v{{ required }} required</span>
      </div>
      <p>This app needs a newer agent before it can show live data.</p>
      <app-update-action />
      <p class="hint">The agent updates itself and restarts automatically — this page continues once it's back.</p>
    </div>
  `,
  styles: [`
    .card {
      display: flex; flex-direction: column; align-items: center; gap: 14px;
      padding: 40px 48px; background: #fff; border: 1px solid #f3d6d6;
      border-radius: 16px; box-shadow: 0 8px 24px rgba(20,30,50,.06); max-width: 460px; text-align: center;
    }
    h1 { margin: 0; font-size: 22px; color: #1f2733; }
    p { margin: 0; color: #5a6472; font-size: 14px; }
    .hint { font-size: 12px; color: #8a93a3; }
    .versions { display: flex; align-items: center; gap: 10px; }
    .chip { padding: 4px 12px; border-radius: 999px; font-size: 13px; font-weight: 600; }
    .chip.installed { background: #eef1f6; color: #5a6472; }
    .chip.required { background: #fdecea; color: #c62828; }
    .arrow { color: #9aa3b2; }
  `],
})
export class RequiredUpdateComponent {
  @Input({ required: true }) installed!: string;
  @Input({ required: true }) required!: string;
}
