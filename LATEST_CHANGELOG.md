## v4.1.3 (patch)

Changes since v4.1.2:

- Compare the submodule patch path as a RelativeFilePath so the test passes on Windows ([@Claude](https://github.com/Claude))
- Pin diff --submodule=short so Patch reports a moved submodule as its gitlink [patch] ([@Claude](https://github.com/Claude))
- Match caller file paths literally, so file[1].txt no longer also matches file1.txt [patch] ([@Claude](https://github.com/Claude))

