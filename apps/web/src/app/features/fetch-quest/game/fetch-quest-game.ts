import { generateMaze, placeTreats } from './maze-generator';
import { key, same, steps, walkable } from './maze';
import type { Direction, Maze, Position } from './maze';

export interface FetchQuestGameState {
  level: number; maze: Maze; playerPosition: Position; treats: Set<string>;
  totalTreats: number; collectedTreats: number; moves: number;
  status: 'ready' | 'playing' | 'completed'; direction: Direction;
}

export class FetchQuestGame {
  state!: FetchQuestGameState;
  private originalTreats: Position[] = [];
  private accumulated = 0;
  private runningSince: number | null = null;
  private hidden = false;
  private readonly now: () => number;
  private readonly random: () => number;

  constructor(now = () => performance.now(), random = Math.random) {
    this.now = now; this.random = random; this.newMaze(1);
  }

  newMaze(level = this.state.level): void {
    const difficulty = Math.min(level, 4) - 1;
    const maze = generateMaze([11, 15, 19, 21][difficulty], this.random);
    this.originalTreats = placeTreats(maze, [3, 5, 7, 10][difficulty], this.random);
    this.state = { level, maze, playerPosition: maze.start, treats: new Set(), totalTreats: 0,
      collectedTreats: 0, moves: 0, status: 'ready', direction: 'down' };
    this.restart();
  }

  restart(): void {
    Object.assign(this.state, { playerPosition: { ...this.state.maze.start },
      treats: new Set(this.originalTreats.map(key)), totalTreats: this.originalTreats.length,
      collectedTreats: 0, moves: 0, status: 'ready', direction: 'down' });
    this.accumulated = 0; this.runningSince = null;
  }

  move(direction: Direction): boolean {
    if (this.hidden || this.state.status === 'completed') return false;
    const d = steps[direction], p = this.state.playerPosition;
    const next = { row: p.row + d.row, column: p.column + d.column };
    if (!walkable(this.state.maze, next)) return false;
    if (this.state.status === 'ready') { this.state.status = 'playing'; this.runningSince = this.now(); }
    this.state.playerPosition = next; this.state.direction = direction; this.state.moves++;
    if (this.state.treats.delete(key(next))) this.state.collectedTreats++;
    if (same(next, this.state.maze.goal)) {
      this.accumulated = this.elapsedMs(); this.runningSince = null; this.state.status = 'completed';
    }
    return true;
  }

  elapsedMs(): number { return this.accumulated + (this.runningSince === null ? 0 : this.now() - this.runningSince); }

  setHidden(hidden: boolean): void {
    if (this.hidden === hidden) return;
    this.hidden = hidden;
    if (hidden) { this.accumulated = this.elapsedMs(); this.runningSince = null; }
    else if (this.state.status === 'playing') this.runningSince = this.now();
  }
}

export function formatTime(ms: number): string {
  const seconds = Math.floor(ms / 1000);
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`;
}
