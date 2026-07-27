---
id: create-gitignore
title: Set up a .gitignore
danger: safe
terms:
  - commit
---

## what

Creates a file called `.gitignore` listing the things git should leave alone — build output,
installed dependencies, editor settings, and secrets.

Anything matching a line in that file stays on your computer and never goes into a [[commit]],
so it never reaches anyone you share the project with.

The file is ordinary text. You can open and edit it whenever you like.

## risks

Files already being tracked by git are not affected by adding them here. `.gitignore` decides
what git *starts* paying attention to, not what it already watches.

If a rule is too broad you might hide a file you meant to save. Everything in the file is
commented, so you can read what each line does before it is written.

## undo

Delete the `.gitignore` file, or open it and remove the lines you do not want.
