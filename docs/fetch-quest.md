# Fetch Quest — Phase 1

Open **Tools → Fetch Quest** (`/tools/fetch-quest`) within the existing authenticated app.
Focus the canvas and press arrows or WASD; on-screen direction buttons also work with touch or keyboard.
Each press moves one cell. Reach the ball to win; bones are optional.
Restart Maze / Replay Level restores the same board and all treats. New Maze regenerates the current level.
Next Maze advances through 11, 15, 19 and 21-cell boards, then continues with new 21-cell boards.
The clock starts on the first successful move and excludes time spent in hidden tabs.
Progress lasts until the page is left or reloaded. No API, storage, or backend changes are involved.

Implementation lives in `apps/web/src/app/features/fetch-quest`:

- `maze-generator.ts`: iterative DFS carving, breadth-first farthest goal, ranked side-path treat placement.
- `fetch-quest-game.ts`: movement, collision, collectibles, progression, replay and active-tab time.
- `game-renderer.ts`: responsive pixel canvas and three-frame directional dog animation.
- `input-controller.ts`: canvas-scoped keys, repeat suppression and listener cleanup.
- Angular page: HUD, controls, modal focus, and lifecycle. Animation runs outside Angular's zone.

Replaceable original placeholder sprites live in `apps/web/public/assets/games/fetch-quest`.

Validation from `apps/web`:

```sh
npm test
npm run build:production
npx playwright test fetch-quest.spec.mjs
```

Browser tests use the existing isolated Angular harness and cover real keyboard/touch play,
victory/replay/progression, focus and 320px layout. Set `PLAYWRIGHT_CHANNEL=msedge` if using installed Edge.
Unit tests check connectivity and farthest goals across 400 seeded mazes plus movement, treats,
completion, restart, timer visibility and input cleanup.
