# Coding standards: tools enforce what they can, one written guide covers the rest

Most of this repo's code is written by AI agents, and agents drift from any rule that nothing checks: Prettier was installed and configured but never run. So every rule a tool can check fails CI: the C# formatter (`dotnet format`), Prettier, and the C# style rules that only run with `EnforceCodeStyleInBuild`. Each tool runs on its defaults, so nobody designs or debates a house style.

What tools cannot check lives in one root `CODING_STANDARDS.md`. `AGENTS.md`, which both Claude Code and Codex read, points to it, and the `code-review` skill's Standards review checks every change against it. The bar is that a person reads any file without hesitation. It is judged in review, as Google does, rather than by line-count limits that chop code up to fit, and named smells with concrete triggers make it checkable.

Existing code is reformatted once by the tools, in mechanical commits listed in `.git-blame-ignore-revs`. Beyond that, a change brings every file it touches up to the standard, and untouched files wait.

## Considered options

- **Rules inside `AGENTS.md`:** rejected, because it would grow past the roughly 200 lines that Anthropic's Claude Code docs say instruction files should stay under to be followed reliably.
- **One guide per language:** deferred, not rejected. It suits a larger codebase where each language has its own philosophy; split the guide when it outgrows one file.
- **One cleanup of all existing code:** rejected as churn that risks breaking working tests. The Area 1 parity tests get rewritten in the redesign anyway.
