# MetricBot — Codex instructions

## Project

MetricBot is a Windows desktop application written in C#/.NET.

## General rules

- Do not rewrite working code without a clear reason.
- Preserve existing functionality unless explicitly asked to change or remove it.
- Preserve the existing UI unless the task specifically concerns the UI.
- Prefer small, isolated changes over large refactorings.
- Do not introduce new dependencies unless they are necessary.
- Do not perform unrelated cleanup or refactoring while implementing a requested change.

## Validation

- Build the project after significant code changes.
- Fix compilation errors introduced by your changes before considering the task complete.
- Do not claim that a change works if it has not been verified.
- Clearly report anything that could not be tested or verified.

## Git

- Do not create commits automatically.
- Do not push to remote repositories automatically.
- Do not change branches unless explicitly requested.
- Do not modify `.gitignore` unless the task requires it.

## Safety

- Do not modify or expose secrets, API keys, passwords, or access tokens.
- Do not delete user data or project files unless explicitly requested.
- Ask before performing destructive or difficult-to-reverse operations.