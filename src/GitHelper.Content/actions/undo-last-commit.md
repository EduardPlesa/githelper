---
id: undo-last-commit
title: Undo last commit
danger: caution
terms: [commit, staging-area]
---
## what
Removes your most recent [[commit]] from the history, but keeps everything it contained
as staged changes in the [[staging-area]]. Nothing you wrote is lost — the save is
undone, not the work.

Use this when you committed too early, used the wrong message, or left a file out.

## risks
If the commit was already sent to the server, undoing it locally puts you out of step
with what is there, which causes trouble on your next send.

The very first commit in a project cannot be undone this way, as there is no earlier
version to step back to.

## undo
Commit again. The changes are still staged and ready.
