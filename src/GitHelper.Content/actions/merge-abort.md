---
id: merge-abort
title: Abandon the merge
danger: caution
terms: [merge, conflict, working-directory]
---
## what
Calls off the [[merge|merge]] and puts your [[working-directory|working files]] back exactly
as they were before it started. {branch} keeps its own commits and {mergingFrom} keeps its
own; nothing that was committed is touched.

## risks
Any [[conflict|conflict]] you have already sorted out by hand is thrown away, and there is
no way to get that work back — it was never committed. If you have spent time on the files,
that time is spent.

The merge itself can always be started again afterwards. The conflicts will be the same
ones, and you will sort them out from the beginning.

## undo
There is nothing to undo. This is itself the undo — it is how you get back to where you
were before the merge began.
