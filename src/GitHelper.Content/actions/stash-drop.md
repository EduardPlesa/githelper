---
id: stash-drop
title: Delete stash
danger: destructive
terms: [stash, commit]
---
## what
Removes this entry from the [[stash]] list for good, without copying its changes back
anywhere first.

## risks
This is the one way stashed work actually disappears. Once dropped, there is no file,
[[commit]], or list entry left holding those changes — they cannot be recovered through
this app.

## undo
There is no undo. Bring the changes back first if there is any chance you still want them.
