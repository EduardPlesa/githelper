---
id: merge
title: Bring this branch's work in
danger: caution
terms: [branch, merge, conflict, fast-forward]
---
## what
Copies the commits from [[branch|branch]] {branchName} into {branch}, so the work done on
both is combined into one history. This is a [[merge|merge]].

If nothing has changed on {branch} since {branchName} split off, git can simply move you
forward — a [[fast-forward|fast-forward]] — and no new commit is made.

## risks
If the two branches changed the same lines of the same file, git cannot decide which
version wins. It stops part-way and hands those files to you: this is a
[[conflict|conflict]], and a banner will appear at the top of the window with what to do
next. Nothing is lost while that banner is showing.

Everything must be committed before you start. Git needs the working files to be a clean
starting point, so it can put them back if you change your mind.

## undo
While the merge is still going, abandon it — everything returns to how it was.

Once it has finished, the merge is an ordinary commit in your history. Undoing it after
that means rewriting history, which this app deliberately does not do.
