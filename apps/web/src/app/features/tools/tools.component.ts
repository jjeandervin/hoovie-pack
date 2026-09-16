import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'hp-tools',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="page">
      <header class="page-heading">
        <div>
          <p class="eyebrow">A little help for your pack</p>
          <h1>Tools</h1>
          <p>Handy resources for life with your dogs.</p>
        </div>
      </header>
      <div class="tools-grid">
        <a class="tool-card" routerLink="/dogipedia">
          <img src="/assets/dogipedia.svg" width="88" height="88" alt="" aria-hidden="true">
          <h2>Dogipedia</h2>
          <p>Get to know dog breeds and explore their traits, personalities, and care needs.</p>
          <span class="text-link">Explore breeds <span aria-hidden="true">&rarr;</span></span>
        </a>
      </div>
    </div>
  `,
  styles: `
    :host { display: block; }
    .tools-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 280px), 360px)); gap: 22px; }
    .tool-card { display: flex; flex-direction: column; align-items: flex-start; gap: 14px; padding: 26px; border: 1px solid var(--line); border-radius: var(--radius-lg); background: var(--paper); color: inherit; text-decoration: none; box-shadow: var(--shadow-sm); transition: box-shadow .2s; }
    .tool-card:hover, .tool-card:focus-visible { box-shadow: var(--shadow-md); }
    .tool-card h2, .tool-card p { margin: 0; }
    .tool-card h2 { font-size: 1.5rem; }
    .tool-card p { color: var(--muted); font-size: .88rem; line-height: 1.7; }
    .tool-card .text-link { margin-top: auto; font-size: .85rem; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ToolsComponent {}
