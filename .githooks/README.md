# Commit history guard

The versioned hooks keep commit authorship consistent and reject attribution trailers from new commit messages. The history checker validates every reachable commit and fails closed for shallow repositories.

Activate the local hook with:

`git config core.hooksPath .githooks`

Run `sh .githooks/check-history` when validating a full clone. Run `sh .githooks/test-history` to exercise clean history, author validation, attribution-trailer rejection, and shallow-history handling.
