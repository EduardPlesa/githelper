---
id: init-repository
title: Start tracking this folder
danger: safe
terms:
  - local-repository
  - commit
---

## what

Creates a [[local-repository|local repository]] in this folder: a hidden `.git` folder where
git stores every version you save from now on.

Your files are not moved, renamed, or changed. Nothing is saved into history yet either —
that happens when you make your first [[commit]].

The new repository starts on a branch called `main`.

## risks

Almost none. Nothing outside the new `.git` folder is touched, and no file you already have is
read, changed, or deleted.

The history starts empty. Git does not go back and record the versions of your files that
existed before this moment, because it never saw them.

## undo

Delete the `.git` folder that was just created. Your own files are untouched by that, and the
folder goes back to being an ordinary folder.
