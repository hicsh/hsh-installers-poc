import { Component } from '@angular/core';
import { AGENT_OS_LABELS, ALL_DOWNLOADS, AgentOS, detectAgentOS, downloadUrlFor } from '../util/agent-os';

/**
 * Shown when no agent answers on localhost. Detects the OS and offers the
 * matching installer as the primary action, with the other platforms listed as
 * smaller alternatives underneath (per the brief). Keeps polling in the
 * background — AgentService reconnects automatically, so this flips to the live
 * view the moment the agent comes up.
 */
@Component({
  selector: 'app-agent-missing',
  standalone: true,
  template: `
    <div class="card">
      <h1>HSH Agent not found</h1>
      <p>It needs to be installed and running on this machine for the live data to appear.</p>

      @if (primary; as p) {
        <a class="btn primary" [href]="p.url" target="_blank" rel="noopener">Download for {{ p.label }}</a>
      }

      <div class="alternatives">
        <span class="alt-label">Other platforms</span>
        <div class="alt-links">
          @for (d of alternatives; track d.os) {
            <a [href]="d.url" target="_blank" rel="noopener">{{ d.label }}</a>
          }
        </div>
      </div>

      <div class="waiting"><span class="dot"></span> Waiting for the agent to start…</div>
    </div>
  `,
  styles: [`
    .card {
      display: flex; flex-direction: column; align-items: center; gap: 14px;
      padding: 40px 48px; background: #fff; border: 1px solid #e3e7ee;
      border-radius: 16px; box-shadow: 0 8px 24px rgba(20,30,50,.06); max-width: 460px; text-align: center;
    }
    h1 { margin: 0; font-size: 22px; color: #1f2733; }
    p { margin: 0; color: #5a6472; font-size: 14px; }
    .btn {
      display: inline-flex; align-items: center; padding: 10px 20px; border-radius: 8px;
      font-size: 14px; font-weight: 600; text-decoration: none;
    }
    .btn.primary { background: #3b6ef5; color: #fff; }
    .alternatives { display: flex; flex-direction: column; align-items: center; gap: 6px; margin-top: 4px; }
    .alt-label { font-size: 11px; text-transform: uppercase; letter-spacing: .08em; color: #9aa3b2; }
    .alt-links { display: flex; flex-wrap: wrap; gap: 14px; justify-content: center; }
    .alt-links a { font-size: 12px; color: #3b6ef5; text-decoration: none; }
    .alt-links a:hover { text-decoration: underline; }
    .waiting { display: flex; align-items: center; gap: 8px; font-size: 13px; color: #8a93a3; margin-top: 6px; }
    .dot { width: 8px; height: 8px; border-radius: 50%; background: #f0ad4e; animation: pulse 1.4s ease-in-out infinite; }
    @keyframes pulse { 0%,100% { opacity: .3; } 50% { opacity: 1; } }
  `],
})
export class AgentMissingComponent {
  private readonly os: AgentOS = detectAgentOS();

  readonly primary = this.os === 'unknown'
    ? null
    : { label: AGENT_OS_LABELS[this.os], url: downloadUrlFor(this.os)! };

  /** All platforms when the OS is unknown, otherwise everything except the detected one. */
  readonly alternatives = ALL_DOWNLOADS.filter((d) => d.os !== this.os);
}
