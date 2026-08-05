---
id: discard-file
title: Discard changes to file
danger: destructive
terms: [working-directory, commit]
---
## what
Throws away every edit you have made to {path} since your last [[commit]], and puts the
file back to that saved version.

## risks
**This permanently deletes your unsaved edits to this file.** They were never committed,
so git has no copy of them anywhere. Nothing in this app or in git can bring them back.

If you are unsure, commit first instead — you can always undo a commit.

## undo
There is no undo. This is one of two actions in this app that can lose work for good,
which is why it asks you twice.
