export interface Position { row: number; column: number; }
export interface Maze { cells: number[][]; start: Position; goal: Position; }
export type Direction = 'up' | 'down' | 'left' | 'right';
export const steps: Record<Direction, Position> = {
  up: { row: -1, column: 0 }, down: { row: 1, column: 0 },
  left: { row: 0, column: -1 }, right: { row: 0, column: 1 }
};
export const key = (p: Position): string => `${p.row},${p.column}`;
export const same = (a: Position, b: Position): boolean => key(a) === key(b);
export const walkable = (maze: Maze, p: Position): boolean => maze.cells[p.row]?.[p.column] === 0;
export const neighbors = (maze: Maze, p: Position): Position[] => Object.values(steps)
  .map(d => ({ row: p.row + d.row, column: p.column + d.column })).filter(n => walkable(maze, n));
