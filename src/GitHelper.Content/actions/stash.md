---
id: stash
title: Set changes aside
danger: safe
terms: [stash, working-directory, commit]
undo: stash-pop
---
## what
Lifts your uncommitted changes off to the side and gives you back a clean
[[working-directory|working directory]], without creating a [[commit]]. Git calls this
shelf a [[stash]].

Only changes to files git already tracks are set aside. Files you have never staged or
committed are left exactly where they are.

## risks
Nothing is lost, but it is out of sight: a file that looks unchanged after this has its
edits sitting in the stash, not gone. Bringing them back is how you see them again.

## undo
Bringing the changes back restores them and removes this entry from the stash list.
