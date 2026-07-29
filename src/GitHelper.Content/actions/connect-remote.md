---
id: connect-remote
title: Connect to GitHub
danger: caution
terms: [remote, origin, github, local-repository]
undo: disconnect-remote
---
## what
Records where this project's online copy lives. Git files the address {remoteUrl} under the
nickname [[origin]], so that sending and getting changes later know where to go.

Nothing is uploaded by this step. It writes an address into this project's settings, and
that is all. Your [[local-repository|project on this computer]] and the copy on
[[github|GitHub]] stay two separate things until you send your work.

That separation is worth knowing: git is the program on your computer that tracks changes,
and GitHub is a company that stores copies of projects. Git works perfectly well without
GitHub. Connecting the two is what gets your work backed up and visible to other people.

## risks
The repository you created on GitHub must be **empty** — no README, no .gitignore, no
licence. If GitHub added any of those, it already has a history of its own, and your first
send will be refused because the two histories have nothing in common.

A wrong address is not noticed here. Git accepts any address that looks like one, and the
mistake only surfaces when you try to send.

Signing in happens the first time you send, not now. Git handles that itself and may open a
browser window for it. This app never sees, asks for, or stores your password or access
token.

## undo
Disconnecting removes the address again. Nothing already sent is affected, and your commits
stay exactly where they are — an address is the only thing this wrote.
