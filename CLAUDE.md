# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See `AGENTS.md` for all project conventions, repository layout, and working-with-AI-agents rules. Everything there applies here too.

## Claude-Specific Notes

- **Presenting options**: Always use the `AskUserQuestion` popup tool when presenting 2+ choices. Never list options in plain text.
- **Branches in cloud sessions**: Work goes on a `feature/<issue#>-<short-slug>` branch cut from `origin/main`, even when the session names a designated `claude/...` branch. Don't ask which to use — this is the standing preference.
