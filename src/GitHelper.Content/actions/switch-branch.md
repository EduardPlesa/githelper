---
id: switch-branch
title: Switch branch
danger: caution
terms: [branch, working-directory]
---
## what
Moves you to [[branch|branch]] {branchName} and replaces your [[working-directory|working files]]
with that branch's versions.

## risks
Files on screen will change. That is expected, not a bug — you are looking at a
different version of the project.

If you have unsaved edits, git will refuse rather than risk mixing them into the other
branch. Commit first.

## undo
Switch back to {branch}. Nothing is lost by moving between branches.
