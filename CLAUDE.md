# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See `AGENTS.md` for all project conventions, repository layout, and working-with-AI-agents rules. Everything there applies here too.

## Claude-Specific Notes

- **Presenting options**: Always use the `AskUserQuestion` popup tool when presenting 2+ choices. Never list options in plain text.
- **Hooks** (`.claude/settings.json`, scripts in `.claude/hooks/`): a session opening on a worktree whose art submodule (`Game/Assets/Art`) is empty checks it out from the main checkout's copy, when that has the art (no download, and nothing in the main checkout itself); every run of that hook appends a line to `init-art.log` in the shared `.git` folder, to tell a hook that never ran from one that ran against the wrong folder (#98). Searches into `Library/`, `Temp/` or `obj/`, and recursive shell `grep`/`find` over the repo root or `Game/` that don't exclude Library, are refused. When a reply ends with `.cs` changes in Core, Core.Tests or Harness, the solution is built in Release, and a failing build must be fixed before handing back (or reported, if it still fails). The same goes for `.cs` changes under `Game/Assets`: Unity compiles them headless (a minute or two; it can't while the Editor has the project open, and says so). Review or switch them off with `/hooks`; after changing the search block, run `pwsh .claude/hooks/Test-BlockUnitySearch.ps1`.
- **Branches in cloud sessions**: Work goes on a `feature/<issue#>-<short-slug>` branch cut from `origin/main`, even when the session names a designated `claude/...` branch. Don't ask which to use — this is the standing preference.
