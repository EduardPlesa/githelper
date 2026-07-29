---
id: disconnect-remote
title: Disconnect from GitHub
danger: caution
terms: [remote, origin, upstream]
---
## what
Forgets the address of this project's [[remote|online copy]]. The nickname [[origin]] stops
pointing anywhere, and sending or getting changes is unavailable until an address is set again.

## risks
Your commits are untouched: this changes an address, not history. Anything already sent
stays on the server, and this app cannot delete it from there.

If you disconnect while work exists only on this computer, that work has no backup until you
connect somewhere and send again.

## undo
Connect again with the same address. Removing the connection also removes the
[[upstream]] link between your branch and the branch on the server, so the first send
afterwards sets that link up again — exactly as it did the first time.
