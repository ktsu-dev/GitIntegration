## v4.3.0 (minor)

Changes since v4.2.0:

- Keep a trailing space in a bare repository's path when opening or discovering it [patch] (#320) (matt-edmondson)
- Read a 0x1F in a commit's subject or signature name without misparsing the log [patch] (#321) (matt-edmondson)
- Report whether a conflicting pull stopped mid-merge or mid-rebase [minor] (#322) (matt-edmondson)
- [patch] Emit --ff with Pull().Merge() so a host's merge.ff=only can't abort it (#308) (matt-edmondson)
- Replace every value when SetRemoteUrl meets a remote with several URLs [patch] (#310) (matt-edmondson)
- Document that UpdateSubmodules().WithDepth has no effect for a local-path submodule [patch] (#312) (matt-edmondson)
- Qualify the branch as refs/heads/ in Push().WithBranch and Pull().WithBranch [patch] (#313) (matt-edmondson)
- Document that an SSO-blocked GitHub token raises GitHostingAuthenticationException with the authorisation URL [patch] (#314) (matt-edmondson)

