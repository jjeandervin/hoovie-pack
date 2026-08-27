import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHostComponent } from './shared/toast-host.component';

@Component({
  selector: 'hp-root',
  standalone: true,
  imports: [RouterOutlet, ToastHostComponent],
  template: `
    <a class="skip-link" href="#main-content">Skip to content</a>
    <router-outlet />
    <hp-toast-host />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppComponent {}
