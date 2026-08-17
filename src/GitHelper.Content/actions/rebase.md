---
id: rebase
title: Bring this branch up to date
danger: caution
terms: [rebase, branch, commit, conflict, upstream]
---
## what
Takes the commits you have made on {branch} and replays them on top of {branchName}, so your
work starts from the latest version of that branch instead of an older one. Git calls this a
[[rebase|rebase]].

Your commits are not moved. Each one is remade as a new [[commit|commit]] with the same
changes, one at a time. That is why this is offered only while your work is still on this
computer: the remade commits are not the same commits, and anyone who already had the old ones
would be left holding a version that no longer exists.

## risks
If a commit being replayed touches the same lines as work already on {branchName}, git stops
and hands that file to you. This is a [[conflict|conflict]], and a banner at the top of the
window will say which commit it stopped on and how many are left. Everything is still
recoverable while that banner is showing — with one exception: **Skip this commit** drops the
commit git stopped on, and that one is gone for good.

Everything must be committed before you start, so git has a clean point to put back if you
change your mind.

If this branch has already been sent to the server it has an [[upstream|upstream]], and this
is refused. Rewriting commits that other people may already have needs a force-push, which
this app does not do.

## undo
While the update is still going, abandon it — your branch returns to exactly how it was.

Once it has finished, your commits have been remade and the originals are no longer on the
branch. Git keeps them for a while, but this app offers no way back to them.
