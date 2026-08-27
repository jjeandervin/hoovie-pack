import { HttpClient } from '@angular/common/http';
import { Directive, ElementRef, EventEmitter, Input, OnDestroy, Output, Renderer2 } from '@angular/core';
import { Subscription, finalize } from 'rxjs';
import { RuntimeConfigService } from '../core/runtime-config.service';

@Directive({
  selector: 'img[hpAuthImage]',
  standalone: true
})
export class AuthImageDirective implements OnDestroy {
  private request?: Subscription;
  private objectUrl?: string;
  private generation = 0;

  constructor(
    private readonly element: ElementRef<HTMLImageElement>,
    private readonly renderer: Renderer2,
    private readonly http: HttpClient,
    private readonly config: RuntimeConfigService
  ) {}

  @Output() readonly authImageError = new EventEmitter<void>();

  @Input({ required: true })
  set hpAuthImage(source: string | null | undefined) {
    this.load(source);
  }

  ngOnDestroy(): void {
    this.request?.unsubscribe();
    this.revokeObjectUrl();
  }

  private load(source: string | null | undefined): void {
    const generation = ++this.generation;
    this.request?.unsubscribe();
    this.revokeObjectUrl();
    this.renderer.addClass(this.element.nativeElement, 'auth-image--loading');
    this.renderer.removeClass(this.element.nativeElement, 'auth-image--error');

    const url = this.config.mediaUrl(source);
    if (!url) {
      this.renderer.removeAttribute(this.element.nativeElement, 'src');
      this.renderer.removeClass(this.element.nativeElement, 'auth-image--loading');
      return;
    }

    const isProtected = url.startsWith('/api/') || url.startsWith(`${this.config.apiBaseUrl}/`);
    if (!isProtected) {
      this.renderer.setAttribute(this.element.nativeElement, 'src', url);
      this.renderer.removeClass(this.element.nativeElement, 'auth-image--loading');
      return;
    }

    this.request = this.http.get(url, { responseType: 'blob' }).pipe(
      finalize(() => {
        if (generation === this.generation) this.renderer.removeClass(this.element.nativeElement, 'auth-image--loading');
      })
    ).subscribe({
      next: (blob) => {
        if (generation !== this.generation) return;
        this.objectUrl = URL.createObjectURL(blob);
        this.renderer.setAttribute(this.element.nativeElement, 'src', this.objectUrl);
      },
      error: () => {
        if (generation !== this.generation) return;
        this.renderer.removeAttribute(this.element.nativeElement, 'src');
        this.renderer.addClass(this.element.nativeElement, 'auth-image--error');
        this.authImageError.emit();
      }
    });
  }

  private revokeObjectUrl(): void {
    if (!this.objectUrl) return;
    URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = undefined;
  }
}
