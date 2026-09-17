import { key, neighbors, same } from './maze';
import type { Maze, Position } from './maze';

export function generateMaze(size: number, random = Math.random): Maze {
  if (size < 5 || size % 2 !== 1 || !Number.isInteger(size)) throw new Error('Maze size must be an odd integer of at least 5.');
  const cells = Array.from({ length: size }, () => Array<number>(size).fill(1));
  const start = { row: 1, column: 1 };
  const maze = { cells, start, goal: start };
  cells[1][1] = 0;
  const stack = [start];
  while (stack.length) {
    const p = stack[stack.length - 1];
    const choices = [[-2, 0], [2, 0], [0, -2], [0, 2]]
      .map(([r, c]) => ({ row: p.row + r, column: p.column + c }))
      .filter(n => n.row > 0 && n.row < size - 1 && n.column > 0 && n.column < size - 1 && cells[n.row][n.column] === 1);
    if (!choices.length) { stack.pop(); continue; }
    const n = choices[Math.floor(random() * choices.length)];
    cells[(p.row + n.row) / 2][(p.column + n.column) / 2] = 0;
    cells[n.row][n.column] = 0;
    stack.push(n);
  }
  const { order } = explore(maze);
  maze.goal = order[order.length - 1];
  return maze;
}

function explore(maze: Maze) {
  const order = [maze.start];
  const parents = new Map<string, Position | null>([[key(maze.start), null]]);
  for (let i = 0; i < order.length; i++) {
    for (const n of neighbors(maze, order[i])) {
      if (parents.has(key(n))) continue;
      parents.set(key(n), order[i]); order.push(n);
    }
  }
  return { order, parents };
}

export function placeTreats(maze: Maze, count: number, random = Math.random): Position[] {
  const { order, parents } = explore(maze);
  const route = new Set<string>();
  let p: Position | null = maze.goal;
  while (p) { route.add(key(p)); p = parents.get(key(p)) ?? null; }
  // Prefer side-path dead ends, then other side paths, with distant cells first.
  return order.filter(n => !same(n, maze.start) && !same(n, maze.goal))
    .map((n, index) => ({ n, rank: (route.has(key(n)) ? 0 : 10000) +
      (neighbors(maze, n).length === 1 ? 1000 : 0) + index + random() * 20 }))
    .sort((a, b) => b.rank - a.rank).slice(0, count).map(item => item.n);
}
