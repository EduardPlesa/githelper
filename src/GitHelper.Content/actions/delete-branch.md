---
id: delete-branch
title: Delete branch
danger: caution
terms: [branch, unmerged-branch, commit]
---
## what
Deletes the [[branch]] {branchName}. The branch name goes away; the [[commit|commits]]
on it are not deleted by this.

## risks
Git checks first. If the branch holds [[unmerged-branch|unmerged work]] that exists
nowhere else, it refuses rather than deleting it — this app never overrides that
refusal, so a branch with work on it cannot be lost here by accident.

You cannot delete the branch you are currently on.

## undo
There is no undo button for this, but the work is not gone. Deleting a branch whose
commits live elsewhere loses nothing.
