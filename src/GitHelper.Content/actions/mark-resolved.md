---
id: mark-resolved
title: Mark as fixed
danger: safe
terms: [conflict, staging-area]
---
## what
Tells git you have finished sorting out {path} by hand, and that the version now on disk is
the one you want.

Open the file in your editor first. Git has written both versions into it, wrapped in
marker lines that start with `<<<<<<<`, `=======` and `>>>>>>>`. Decide what the file should
say, delete the markers, and save. Then use this.

## risks
This trusts you. Git does not check whether the marker lines are gone — if you mark a file
fixed while they are still in it, those markers become part of the merge, and the file will
be broken in a way that looks like ordinary text.

Read the file once more before using this.

## undo
Nothing is committed yet, so nothing is final. You can keep editing {path} and mark it
fixed again, and the newer version replaces the older one in the [[staging-area|staging area]].

Abandoning the whole merge also undoes this, along with every other [[conflict|conflict]]
you have sorted out.
