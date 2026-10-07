## v4.1.6 (patch)

Changes since v4.1.5:

- Move the partial-rename round-trip test away from the end of the class ([@Claude](https://github.com/Claude))
- Write a partial rename patch as a plain change to the new path ([@Claude](https://github.com/Claude))
- Report conflicted binary files and modify/delete conflicts as unmerged in Patch() ([@Claude](https://github.com/Claude))
- Clear inherited GIT_DIR, GIT_INDEX_FILE and GIT_DIFF_OPTS before running git [patch] ([@Claude](https://github.com/Claude))

