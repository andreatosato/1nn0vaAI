# Golden requests

Instructions and tool definitions captured from the real LIVE measurements in `presentazione/misurazioni-2026-09-25-unbounded` (first model call of each agent, `good` prompt profile, no prompt blocks).
The tests assert that the restructured agents still send exactly these instructions and tools.

- `inline-router`: run `45d6f7b985d04674afc6b5d728ab84e3`, call `bcb3b67b166b4144ae027066d45b37fe`
- `skills-router`: run `c49ef76a32834b418f908abf03870806`, call `1f46ca7e8cc9498c93acb8e553094cd1` (recaptured in `presentazione/misurazioni-skill-remote-unbounded` after the move to remote skill sites)
- `a2a-router`: run `94fc8721fe4d4a1e9591c9e8549702bf`, call `6bcffdfcaa1b4ae8bac80dc3eba86b25`
- `a2a-catalog`: run `94fc8721fe4d4a1e9591c9e8549702bf`, call `a3358a59e4cb4e9f94c5f4487ec66ac0`
- `a2a-orders`: run `94fc8721fe4d4a1e9591c9e8549702bf`, call `2a69451758894025886b83e56240c5db`
- `a2a-returns`: run `94fc8721fe4d4a1e9591c9e8549702bf`, call `5817fbf572614abda5e161cc55137d58`
