---
id: stash-apply
title: Copy back stashed changes
danger: caution
terms: [stash, working-directory]
---
## what
Copies the changes from this [[stash]] entry back into your [[working-directory|working
directory]], the same as bringing them back, but leaves the entry on the list afterwards
instead of removing it.

Useful when you want the same changes in more than one place without giving up the copy on
the shelf.

## risks
Only offered when your [[working-directory|working directory]] is clean, so this can never
land on top of other unsaved edits. It can still clash with commits made since the changes
were set aside — if that happens, this app puts your files back exactly as they were and
keeps the stash, rather than leaving you with a half-finished merge.

## undo
There is no undo button for this specific step. Deleting the stash afterwards removes the
shelved copy; the changes just applied to your files are unaffected by that.
