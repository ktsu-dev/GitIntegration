## v4.2.1 (patch)

Changes since v4.2.0:

- [patch] Emit --ff with Pull().Merge() so a host's merge.ff=only can't abort it (#308) (matt-edmondson)
- Replace every value when SetRemoteUrl meets a remote with several URLs [patch] (#310) (matt-edmondson)
- Document that UpdateSubmodules().WithDepth has no effect for a local-path submodule [patch] (#312) (matt-edmondson)
- Qualify the branch as refs/heads/ in Push().WithBranch and Pull().WithBranch [patch] (#313) (matt-edmondson)
- Document that an SSO-blocked GitHub token raises GitHostingAuthenticationException with the authorisation URL [patch] (#314) (matt-edmondson)

