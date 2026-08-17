---
id: rebase-continue
title: Carry on with the update
danger: caution
terms: [rebase, commit, conflict]
---
## what
Finishes replaying the commit git stopped on, and moves to the next one. If that was the last
one, the [[rebase|update]] is complete and your branch now starts from {rebasingOnto}.

## risks
This is only available once every [[conflict|conflict]] has been marked fixed, so the risk is
not that it fails — it is that a file was marked fixed too early. What goes into the remade
[[commit|commit]] is exactly what is on disk right now.

There may be more stops after this one. The banner says how many commits are left.

## undo
There is no undo for this single step. Abandoning the whole update is still offered until the
last commit has been replayed, and that puts the branch back as it was.
