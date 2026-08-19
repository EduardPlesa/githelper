---
id: rebase-abort
title: Abandon the update
danger: caution
terms: [rebase, conflict, working-directory]
---
## what
Calls off the [[rebase|update]] and puts your branch and your
[[working-directory|working files]] back exactly as they were before it started. The commits
already replayed are discarded, and your original commits come back.

## risks
Any [[conflict|conflict]] you have already sorted out by hand is thrown away, and there is no
way to get that work back — it was never committed.

The update can be started again afterwards. The same conflicts will come up, and you will sort
them out from the beginning.

## undo
There is nothing to undo. This is itself the undo — it is how you get back to where you were
before the update began.
