---
id: file-changes
title: What changed in this file
terms:
  - diff
  - hunk
  - staging-area
---

## what

This is a [[diff]]: only the lines that changed, not the whole file. A line starting with a
plus is one you added, a line starting with a minus is one you took away, and the plain lines
around them are there so you can see where the change sits.

Each block starting with @@ is a [[hunk]] — one run of changes and its surroundings. A file
edited in two distant places shows two of them.

There are two different things you can look at, and they answer different questions. Changes
you have not staged yet are the edits sitting in your files right now. Changes you have staged
are the ones already put in the [[staging-area]], waiting to go into your next commit. A file
can have both at once, if you staged it and then kept editing.
