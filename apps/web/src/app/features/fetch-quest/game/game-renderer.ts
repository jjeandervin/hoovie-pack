import type { FetchQuestGameState } from './fetch-quest-game';

/** Sprites use a 16px pixel grid; artwork can be replaced independently of the game. */
export class GameRenderer {
  private readonly context: CanvasRenderingContext2D;
  private readonly canvas: HTMLCanvasElement;
  private readonly sprites = new Map<string, HTMLImageElement>();
  private dirty = true;
  private movingUntil = 0;
  private wasMoving = false;
  private readonly resize: ResizeObserver;

  constructor(canvas: HTMLCanvasElement) {
    this.canvas = canvas;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Canvas is unavailable in this browser.');
    this.context = context;
    this.resize = new ResizeObserver(() => this.invalidate());
    this.resize.observe(canvas);
    for (const name of ['golden-retriever', 'tennis-ball', 'treat']) {
      const img = new Image();
      img.onload = () => { this.sprites.set(name, img); this.invalidate(); };
      img.src = `/assets/games/fetch-quest/${name}.svg`;
    }
  }

  invalidate(moving = false): void {
    this.dirty = true;
    if (moving) this.movingUntil = performance.now() + 180;
    else this.movingUntil = 0;
  }

  render(state: FetchQuestGameState, time: number): void {
    const moving = time < this.movingUntil;
    if (!this.dirty && !moving && !this.wasMoving) return;
    this.wasMoving = moving; this.dirty = false;
    const size = state.maze.cells.length;
    const tile = Math.max(1, Math.floor(this.canvas.clientWidth * (window.devicePixelRatio || 1) / size));
    if (this.canvas.width !== size * tile) this.canvas.width = this.canvas.height = size * tile;
    const c = this.context;
    c.imageSmoothingEnabled = false;
    state.maze.cells.forEach((row, r) => row.forEach((wall, col) => {
      const x = col * tile, y = r * tile;
      c.fillStyle = wall ? '#284d36' : '#b8d788'; c.fillRect(x, y, tile, tile);
      if (wall) {
        c.fillStyle = '#416d43'; c.fillRect(x + 1, y + 1, tile - 2, tile - 5);
        c.fillStyle = '#56834c'; c.fillRect(x + 3, y + 3, tile - 7, 3);
        c.fillStyle = '#365e39'; c.fillRect(x + 13, y + 11, 6, 5);
      } else {
        c.fillStyle = '#a5c879'; c.fillRect(x + (r % 3) * 4 + 4, y + 15, 2, 3);
      }
    }));
    const sprite = (name: string, row: number, column: number) => {
      const img = this.sprites.get(name);
      if (img) c.drawImage(img, column * tile, row * tile, tile, tile);
    };
    for (const treat of state.treats) {
      const [r, col] = treat.split(',').map(Number); sprite('treat', r, col);
    }
    sprite('tennis-ball', state.maze.goal.row, state.maze.goal.column);
    const dog = this.sprites.get('golden-retriever');
    if (dog) {
      const p = state.playerPosition;
      c.save(); c.translate((p.column + .5) * tile, (p.row + .5) * tile);
      c.rotate(({ down: 0, up: Math.PI, left: Math.PI / 2, right: -Math.PI / 2 })[state.direction]);
      const frame = moving ? 1 + Math.floor(time / 65) % 2 : 0;
      c.drawImage(dog, frame * 16, 0, 16, 16, -tile / 2, -tile / 2, tile, tile);
      c.restore();
    }
  }

  destroy(): void { this.resize.disconnect(); }
}
