---
id: create-tag
title: Tag this point
danger: safe
terms: [tag, branch, commit]
undo: delete-tag
---
## what
Marks the [[commit]] you are on right now with the label {tagName}. Unlike a [[branch]], a
[[tag]] never moves — it keeps pointing at this exact commit even after you keep working and
make new ones.

This only tags the commit you currently have checked out. This version of the app has no
way to pick an earlier commit to tag instead.

## risks
Nothing else changes. No files are touched and no commit is created — this only adds a
named pointer.

A tag name has to be unique: git will not let you reuse one that already exists here.

## undo
Deleting the tag removes the label. The commit it pointed to, and everything on it, stays
exactly as it was.
