import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, NgZone, OnDestroy, ViewChild, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FetchQuestGame, formatTime } from './game/fetch-quest-game';
import { GameRenderer } from './game/game-renderer';
import { InputController } from './game/input-controller';
import type { Direction } from './game/maze';

@Component({
  selector: 'hp-fetch-quest-page', standalone: true, imports: [RouterLink],
  templateUrl: './fetch-quest-page.component.html', styleUrl: './fetch-quest-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FetchQuestPageComponent implements AfterViewInit, OnDestroy {
  @ViewChild('board', { static: true }) board!: ElementRef<HTMLCanvasElement>;
  @ViewChild('victory') victory?: ElementRef<HTMLDialogElement>;
  private readonly zone = inject(NgZone);
  private readonly game = new FetchQuestGame();
  readonly state = signal({ ...this.game.state });
  readonly time = signal('00:00');
  readonly error = signal('');
  private renderer?: GameRenderer;
  private input?: InputController;
  private frame = 0;
  private readonly visibility = () => this.game.setHidden(document.hidden);

  ngAfterViewInit(): void {
    this.zone.runOutsideAngular(() => {
      try { this.renderer = new GameRenderer(this.board.nativeElement); }
      catch { this.zone.run(() => this.error.set('Your browser could not open the game canvas. Please try another browser.')); return; }
      this.input = new InputController(this.board.nativeElement, direction => this.move(direction));
      document.addEventListener('visibilitychange', this.visibility);
      this.visibility();
      const tick = (now: number) => {
        this.renderer?.render(this.game.state, now);
        const value = formatTime(this.game.elapsedMs());
        if (value !== this.time()) this.zone.run(() => this.time.set(value));
        this.frame = requestAnimationFrame(tick);
      };
      this.frame = requestAnimationFrame(tick);
    });
  }

  move(direction: Direction): void {
    if (!this.game.move(direction)) return;
    this.renderer?.invalidate(true);
    this.zone.run(() => {
      this.state.set({ ...this.game.state }); this.time.set(formatTime(this.game.elapsedMs()));
      if (this.game.state.status === 'completed') this.victory?.nativeElement.showModal();
    });
  }

  reset(action: 'restart' | 'new' | 'next'): void {
    this.victory?.nativeElement.close();
    if (action === 'restart') this.game.restart();
    else this.game.newMaze(this.game.state.level + (action === 'next' ? 1 : 0));
    this.state.set({ ...this.game.state }); this.time.set('00:00');
    this.renderer?.invalidate(); this.board.nativeElement.focus({ preventScroll: true });
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.frame); this.input?.destroy(); this.renderer?.destroy();
    document.removeEventListener('visibilitychange', this.visibility);
    this.victory?.nativeElement.close();
  }
}
