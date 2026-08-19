---
id: merge-continue
title: Finish the merge
danger: caution
terms: [merge, commit, conflict]
---
## what
Completes the [[merge|merge]] you are part-way through and saves the result as a
[[commit|commit]] on {branch}.

Git writes the message itself — a merge commit records which two histories were joined
rather than what you changed, so there is nothing for you to describe.

## risks
This is only available once every [[conflict|conflict]] has been marked fixed, so the risk
is not that it fails — it is that a file was marked fixed too early. What lands in the
commit is whatever was marked fixed, not necessarily what is on disk right now. If you edit
a file again after marking it fixed, mark it fixed again before finishing, or that later
edit is left out.

## undo
The merge becomes an ordinary commit, so this is the point of no return for the merge as a
whole: abandoning is no longer offered afterwards.

The work itself is safe either way. Both branches still exist, and both still hold their
own commits.
