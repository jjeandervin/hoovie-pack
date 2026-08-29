import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ToastService } from '../core/toast.service';

@Component({
  selector: 'hp-toast-host',
  standalone: true,
  templateUrl: './toast-host.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ToastHostComponent {
  constructor(readonly toasts: ToastService) {}
}
