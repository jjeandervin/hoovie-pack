import { HttpClient } from '@angular/common/http';
import { Directive, ElementRef, EventEmitter, Input, OnDestroy, Output, Renderer2 } from '@angular/core';
import { Subscription, finalize } from 'rxjs';
import { FileDownloadResponse } from '../core/models';
import { RuntimeConfigService } from '../core/runtime-config.service';

@Directive({
  selector: 'img[hpAuthImage]',
  standalone: true
})
export class AuthImageDirective implements OnDestroy {
  private request?: Subscription;
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
  }

  private load(source: string | null | undefined): void {
    const generation = ++this.generation;
    this.request?.unsubscribe();
    this.renderer.addClass(this.element.nativeElement, 'auth-image--loading');
    this.renderer.removeClass(this.element.nativeElement, 'auth-image--error');

    const url = this.config.mediaUrl(source);
    if (!url) {
      this.renderer.removeAttribute(this.element.nativeElement, 'src');
      this.renderer.removeClass(this.element.nativeElement, 'auth-image--loading');
      return;
    }

    const isProtected = this.config.isApiUrl(url);
    if (!isProtected) {
      this.renderer.setAttribute(this.element.nativeElement, 'src', url);
      this.renderer.removeClass(this.element.nativeElement, 'auth-image--loading');
      return;
    }

    this.request = this.http.get<FileDownloadResponse>(url).pipe(
      finalize(() => {
        if (generation === this.generation) this.renderer.removeClass(this.element.nativeElement, 'auth-image--loading');
      })
    ).subscribe({
      next: (download) => {
        if (generation !== this.generation) return;
        if (!download.downloadUrl) {
          this.handleError(generation);
          return;
        }
        this.renderer.setAttribute(this.element.nativeElement, 'src', download.downloadUrl);
      },
      error: () => this.handleError(generation)
    });
  }

  private handleError(generation: number): void {
    if (generation !== this.generation) return;
    this.renderer.removeAttribute(this.element.nativeElement, 'src');
    this.renderer.addClass(this.element.nativeElement, 'auth-image--error');
    this.authImageError.emit();
  }
}
