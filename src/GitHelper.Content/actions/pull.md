---
id: pull
title: Get changes from the server
danger: caution
terms: [remote, upstream, fast-forward, commit]
---
## what
Downloads new commits from {upstream} and adds them to your branch. You are currently
{behind} commit(s) behind.

This app only does the simple case, called a [[fast-forward]]: the server's work is
placed on top of yours. If both you and someone else have made commits, git will stop
and tell you rather than combining the two histories on its own.

## risks
If it refuses, nothing has happened and nothing is broken — it means both sides have
new work, and combining them is a decision you should make deliberately.

## undo
There is nothing to undo when it refuses. When it succeeds, you have simply received
[[commit|commits]] other people already made.
