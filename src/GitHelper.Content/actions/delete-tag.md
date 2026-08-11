---
id: delete-tag
title: Delete tag
danger: caution
terms: [tag, branch, commit]
---
## what
Removes the label {tagName}. Unlike deleting a [[branch]], git does not check first whether
anything else still needs it — a [[tag]] is just a name, so removing it always succeeds.

## risks
The [[commit]] the tag pointed to is not affected. Only the name goes away.

If this tag has already been shared — for example, pushed to GitHub — removing it here does
not remove it there. This app only manages tags on this computer.

## undo
There is no undo button for this. If you still know which commit it pointed to, you can
create a new tag with the same name pointing at it again.
