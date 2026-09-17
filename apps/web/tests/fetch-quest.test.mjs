import assert from 'node:assert/strict';
import test from 'node:test';
import { build } from 'esbuild';

const result = await build({ stdin: { contents: `export * from './src/app/features/fetch-quest/game/maze'; export * from './src/app/features/fetch-quest/game/maze-generator'; export * from './src/app/features/fetch-quest/game/fetch-quest-game'; export * from './src/app/features/fetch-quest/game/input-controller';`, resolveDir: process.cwd() }, bundle: true, write: false, format: 'esm', platform: 'node' });
const { FetchQuestGame, generateMaze, placeTreats, key, neighbors, steps, walkable, formatTime, InputController } = await import(`data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString('base64')}`);

function seeded(seed) { return () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 4294967296; }; }
function paths(maze, start = maze.start) {
  const queue = [start], routes = new Map([[key(start), []]]);
  for (const p of queue) for (const [direction, d] of Object.entries(steps)) {
    const n = { row: p.row + d.row, column: p.column + d.column };
    if (walkable(maze, n) && !routes.has(key(n))) { routes.set(key(n), [...routes.get(key(p)), direction]); queue.push(n); }
  }
  return routes;
}

test('all maze sizes are connected, have farthest reachable goals and valid unique treats across 100 seeds', () => {
  for (const [size, count] of [[11, 3], [15, 5], [19, 7], [21, 10]]) for (let seed = 0; seed < 100; seed++) {
    const random = seeded(seed), maze = generateMaze(size, random), routes = paths(maze);
    assert.equal(maze.cells.length, size); assert.ok(walkable(maze, maze.start)); assert.ok(walkable(maze, maze.goal));
    assert.equal(routes.size, maze.cells.flat().filter(c => c === 0).length);
    assert.equal(routes.get(key(maze.goal)).length, Math.max(...[...routes.values()].map(r => r.length)));
    const treats = placeTreats(maze, count, random);
    assert.equal(treats.length, count); assert.equal(new Set(treats.map(key)).size, count);
    for (const treat of treats) { assert.ok(walkable(maze, treat)); assert.notEqual(key(treat), key(maze.start)); assert.notEqual(key(treat), key(maze.goal)); }
  }
});

test('walls do not start timer or count moves; open cells move exactly once', () => {
  let now = 0; const game = new FetchQuestGame(() => now, seeded(1));
  assert.equal(game.move('up'), false); assert.equal(game.state.moves, 0);
  now = 5000; assert.equal(game.elapsedMs(), 0);
  const direction = paths(game.state.maze).get(key(neighbors(game.state.maze, game.state.playerPosition)[0]))[0];
  assert.equal(game.move(direction), true); assert.equal(game.state.moves, 1);
  now += 1200; assert.equal(game.elapsedMs(), 1200);
});

test('treats collect once; restart restores original terrain, treats and counters', () => {
  let now = 0; const game = new FetchQuestGame(() => now, seeded(4));
  const maze = game.state.maze, original = [...game.state.treats], terrain = JSON.stringify(maze);
  const route = paths(maze).get(original[0]);
  route.forEach(d => game.move(d));
  const collected = game.state.collectedTreats; assert.ok(collected >= 1); assert.ok(!game.state.treats.has(original[0]));
  const reverse = { up: 'down', down: 'up', left: 'right', right: 'left' };
  game.move(reverse[route.at(-1)]); game.move(route.at(-1)); assert.equal(game.state.collectedTreats, collected);
  now = 2000; game.restart();
  assert.equal(game.state.maze, maze); assert.equal(JSON.stringify(maze), terrain);
  assert.deepEqual(game.state.playerPosition, maze.start); assert.deepEqual([...game.state.treats], original);
  assert.equal(game.state.moves, 0); assert.equal(game.state.collectedTreats, 0); assert.equal(game.elapsedMs(), 0); assert.equal(game.state.status, 'ready');
});

test('goal completes without collecting every treat and freezes time and movement', () => {
  let now = 0; const game = new FetchQuestGame(() => now, seeded(1));
  for (const d of paths(game.state.maze).get(key(game.state.maze.goal))) { game.move(d); now += 100; }
  assert.equal(game.state.status, 'completed');
  assert.ok(game.state.collectedTreats < game.state.totalTreats);
  const moves = game.state.moves, elapsed = game.elapsedMs(), position = game.state.playerPosition;
  now += 10000; for (const d of Object.keys(steps)) assert.equal(game.move(d), false);
  assert.equal(game.state.moves, moves); assert.deepEqual(game.state.playerPosition, position); assert.equal(game.elapsedMs(), elapsed);
});

test('hidden tab time is excluded and levels progress to capped sizes with fresh mazes', () => {
  let now = 0; const game = new FetchQuestGame(() => now, seeded(5));
  game.move(paths(game.state.maze).get(key(game.state.maze.goal))[0]); now = 1000; game.setHidden(true);
  now = 10000; assert.equal(game.elapsedMs(), 1000); assert.equal(game.move('down'), false);
  game.setHidden(false); now = 11000; assert.equal(game.elapsedMs(), 2000);
  for (let level = 1; level <= 6; level++) {
    const previous = game.state.maze; game.newMaze(level);
    assert.notEqual(game.state.maze, previous); assert.equal(game.state.maze.cells.length, [11, 15, 19, 21][Math.min(level, 4) - 1]);
    assert.equal(game.state.totalTreats, [3, 5, 7, 10][Math.min(level, 4) - 1]); assert.equal(game.elapsedMs(), 0);
  }
  assert.equal(formatTime(92000), '01:32');
});

test('input supports all directions, ignores repeats and modifiers, and removes its listener', () => {
  let listener; const moves = []; const canvas = { addEventListener: (_, fn) => listener = fn, removeEventListener: (_, fn) => assert.equal(fn, listener) };
  const input = new InputController(canvas, d => moves.push(d));
  for (const key of ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'w', 'S', 'a', 'D']) {
    let prevented = false; listener({ key, preventDefault: () => prevented = true }); assert.ok(prevented);
  }
  assert.deepEqual(moves, ['up', 'down', 'left', 'right', 'up', 'down', 'left', 'right']);
  listener({ key: 'w', repeat: true, preventDefault() {} }); listener({ key: 'w', ctrlKey: true }); listener({ key: 'Tab' });
  assert.equal(moves.length, 8); input.destroy();
});
