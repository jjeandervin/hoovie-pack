import type { Direction } from './maze';

const bindings: Record<string, Direction> = {
  ArrowUp: 'up', w: 'up', ArrowDown: 'down', s: 'down',
  ArrowLeft: 'left', a: 'left', ArrowRight: 'right', d: 'right'
};

export class InputController {
  private readonly canvas: HTMLCanvasElement;
  private readonly onKey: (event: KeyboardEvent) => void;
  constructor(canvas: HTMLCanvasElement, move: (direction: Direction) => void) {
    this.canvas = canvas;
    this.onKey = event => {
      if (event.altKey || event.ctrlKey || event.metaKey) return;
      const direction = bindings[event.key.length === 1 ? event.key.toLowerCase() : event.key];
      if (!direction) return;
      event.preventDefault();
      if (!event.repeat) move(direction);
    };
    canvas.addEventListener('keydown', this.onKey);
  }
  destroy(): void { this.canvas.removeEventListener('keydown', this.onKey); }
}
