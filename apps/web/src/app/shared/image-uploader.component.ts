import { ChangeDetectionStrategy, Component, OnDestroy, input, output, signal } from '@angular/core';

interface PreviewFile {
  file: File;
  url: string;
}

@Component({
  selector: 'hp-image-uploader',
  standalone: true,
  templateUrl: './image-uploader.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ImageUploaderComponent implements OnDestroy {
  readonly maxFiles = input(4);
  readonly disabled = input(false);
  readonly filesChange = output<File[]>();
  readonly previews = signal<PreviewFile[]>([]);
  readonly error = signal('');

  onSelect(event: Event): void {
    const inputElement = event.target as HTMLInputElement;
    const candidates = Array.from(inputElement.files ?? []);
    inputElement.value = '';
    this.error.set('');

    const available = this.maxFiles() - this.previews().length;
    if (candidates.length > available) {
      this.error.set(`Choose no more than ${this.maxFiles()} photos.`);
    }

    for (const file of candidates.slice(0, available)) {
      if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
        this.error.set(`${file.name} is not a supported image type.`);
        continue;
      }
      if (file.size > 10 * 1024 * 1024) {
        this.error.set(`${file.name} is larger than 10 MB.`);
        continue;
      }
      this.previews.update((previews) => [...previews, { file, url: URL.createObjectURL(file) }]);
    }
    this.emitFiles();
  }

  remove(index: number): void {
    const preview = this.previews()[index];
    if (preview) URL.revokeObjectURL(preview.url);
    this.previews.update((previews) => previews.filter((_item, itemIndex) => itemIndex !== index));
    this.error.set('');
    this.emitFiles();
  }

  ngOnDestroy(): void {
    this.previews().forEach((preview) => URL.revokeObjectURL(preview.url));
  }

  private emitFiles(): void {
    this.filesChange.emit(this.previews().map((preview) => preview.file));
  }
}
