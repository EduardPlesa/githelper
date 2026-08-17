---
id: rebase-skip
title: Skip this commit
danger: destructive
terms: [rebase, commit]
---
## what
Drops the [[commit|commit]] git stopped on and carries on with the rest. The changes in that
commit do not end up on your branch at all.

This is worth doing when the commit turns out to be unnecessary — most often because the same
change is already on the other branch, so replaying it would add nothing.

## risks
The commit is gone from your branch. Everything in it goes with it, and this app offers no way
to bring it back.

If you are not certain the change is already present, abandon the [[rebase|update]] instead
and look before deciding.

## undo
There is no undo. Git keeps unreferenced commits for a while, but reaching them means using
git directly, outside this app.

## consequence
**This drops the commit git stopped on, and everything in it, from your branch.** This app
offers no way to bring it back.
