---
id: commit
title: Commit
danger: caution
terms: [commit, staging-area, branch]
undo: undo-last-commit
---
## what
Saves the {stagedCount} staged file(s) as a new [[commit]] on [[branch|branch]] {branch}.
This is the point at which your work becomes part of the permanent history.

Staged files: {stagedFileList}

## risks
Only staged changes are saved. Anything you edited but did not stage stays unsaved, so
check that nothing you wanted is being left behind.

The description you write is kept forever and is what you will scan through later, so a
few specific words beat "update".

## undo
Undo the last commit. The commit disappears and all the work comes back, staged and safe.
