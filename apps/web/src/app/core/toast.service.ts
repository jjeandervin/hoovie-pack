import { Injectable, signal } from '@angular/core';

export interface ToastMessage {
  id: number;
  text: string;
  tone: 'success' | 'error' | 'info';
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly messagesSignal = signal<ToastMessage[]>([]);
  private nextId = 1;
  readonly messages = this.messagesSignal.asReadonly();

  show(text: string, tone: ToastMessage['tone'] = 'info'): void {
    const message = { id: this.nextId++, text, tone };
    this.messagesSignal.update((messages) => [...messages, message]);
    window.setTimeout(() => this.dismiss(message.id), 4200);
  }

  success(text: string): void {
    this.show(text, 'success');
  }

  error(text: string): void {
    this.show(text, 'error');
  }

  dismiss(id: number): void {
    this.messagesSignal.update((messages) => messages.filter((message) => message.id !== id));
  }
}
