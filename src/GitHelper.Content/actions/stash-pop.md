---
id: stash-pop
title: Bring back stashed changes
danger: caution
terms: [stash, working-directory]
---
## what
Copies the changes from this [[stash]] entry back into your [[working-directory|working
directory]] and removes the entry from the list — the shelf and the changes on it, put
back at the same time.

## risks
Only offered when your [[working-directory|working directory]] is clean, so this can never
land on top of other unsaved edits. It can still clash with commits made since the changes
were set aside — if that happens, this app puts your files back exactly as they were and
keeps the stash, rather than leaving you with a half-finished merge.

## undo
There is no undo button for this specific step, but the change it makes is exactly the
opposite of stashing — setting the same changes aside again gets back to where you started.
