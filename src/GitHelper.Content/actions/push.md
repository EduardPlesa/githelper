---
id: push
title: Send changes to the server
danger: caution
terms: [remote, upstream, commit, branch]
---
## what
Uploads your {ahead} unsent [[commit|commit(s)]] from [[branch|branch]] {branch} to the
[[remote|server]]. Once this succeeds, your work is backed up and other people can see it.

If this branch has no [[upstream]] yet, this also sets one up, so future sends know
where to go.

## risks
What you send becomes visible to everyone with access to the project, so it is worth a
look at what is in your commits first.

If someone else has pushed work you do not have, git will refuse. Get their changes
first, then send yours.

## undo
Sending cannot be taken back from inside this app. Anyone may already have downloaded
it, so the normal fix is a new commit that corrects the problem.
