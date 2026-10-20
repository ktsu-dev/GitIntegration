// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System;
using System.Reflection;

using ktsu.Semantics.Strings;

[TestClass]
public class SemanticTypeTests
{
	[TestMethod]
	public void GitCommitShaAcceptsFullSha()
	{
		GitCommitSha sha = GitCommitSha.Create("3f2a1b4c5d6e7f8091a2b3c4d5e6f708192a3b4c");

		Assert.AreEqual("3f2a1b4c5d6e7f8091a2b3c4d5e6f708192a3b4c", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaAcceptsSha256ObjectId()
	{
		// A repository created with --object-format=sha256 emits 64 hex characters.
		const string sha256 = "5b4c3d2e1f009a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a4b";

		GitCommitSha sha = GitCommitSha.Create(sha256);

		Assert.AreEqual(sha256, sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaRejectsValueLongerThanSha256()
	{
		Assert.IsFalse(GitCommitSha.TryCreate(new string('a', 65), out GitCommitSha? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitCommitShaAcceptsAbbreviatedSha()
	{
		GitCommitSha sha = GitCommitSha.Create("3f2a1b4");

		Assert.AreEqual("3f2a1b4", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaLowercasesUppercaseInput()
	{
		GitCommitSha sha = GitCommitSha.Create("3F2A1B4C");

		Assert.AreEqual("3f2a1b4c", sha.WeakString);
	}

	[TestMethod]
	public void GitCommitShaRejectsNonHexadecimal()
	{
		Assert.IsFalse(GitCommitSha.TryCreate("zzzzzzz", out GitCommitSha? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitCommitShaRejectsTooShortValue()
	{
		Assert.IsFalse(GitCommitSha.TryCreate("3f2", out GitCommitSha? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitBranchNameRejectsWhitespaceOnlyValue()
	{
		Assert.IsFalse(GitBranchName.TryCreate("   ", out GitBranchName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void GitBranchNameAcceptsSlashSeparatedName()
	{
		GitBranchName branch = GitBranchName.Create("feature/git-v2");

		Assert.AreEqual("feature/git-v2", branch.WeakString);
	}

	[TestMethod]
	[DataRow("-f")]
	[DataRow("--upload-pack=calc.exe")]
	[DataRow("-")]
	public void GitBranchNameRejectsLeadingDash(string value)
	{
		Assert.IsFalse(GitBranchName.TryCreate(value, out GitBranchName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	[DataRow("a..b")] // two consecutive dots
	[DataRow("x y")] // a space
	[DataRow("a\tb")] // a control character
	[DataRow("a\u007fb")] // DEL
	[DataRow("a~1")] // ~
	[DataRow("a^b")] // ^
	[DataRow("a:b")] // :
	[DataRow("a?b")] // ?
	[DataRow("a*b")] // *
	[DataRow("a[b")] // [
	[DataRow("a\\b")] // a backslash
	[DataRow("a@{b")] // @{
	[DataRow("a//b")] // an empty component
	[DataRow("/a")] // a leading slash
	[DataRow("a/")] // a trailing slash
	[DataRow("a.")] // a trailing dot
	[DataRow(".a")] // a component starting with a dot
	[DataRow("a/.b")] // a later component starting with a dot
	[DataRow("foo.lock")] // a component ending in .lock
	[DataRow("foo.lock/bar")] // an inner component ending in .lock
	public void GitBranchNameRejectsNamesGitForbids(string value)
	{
		Assert.IsFalse(GitBranchName.TryCreate(value, out GitBranchName? result));
		Assert.IsNull(result);
		Assert.ThrowsExactly<ArgumentException>(() => GitBranchName.Create(value));
	}

	[TestMethod]
	[DataRow("main")]
	[DataRow("feature/git-v2")]
	[DataRow("origin/main")]
	[DataRow("@")] // git creates refs/heads/@, and status reports it as branch.head @
	[DataRow("a@b")]
	[DataRow("a.lock.b")]
	[DataRow("release/1.2")]
	[DataRow("(detached)")]
	[DataRow("caf\u00e9/\u00fcn\u00ef")]
	public void GitBranchNameAcceptsNamesGitAccepts(string value)
	{
		// Parsers build GitBranchName from git's own output, so anything git accepts must still
		// construct, or reading that repository would throw GitParseException.
		Assert.AreEqual(value, GitBranchName.Create(value).WeakString);
	}

	[TestMethod]
	[DataRow("-f")]
	[DataRow("--upload-pack=calc.exe")]
	public void GitRefNameRejectsLeadingDash(string value)
	{
		Assert.IsFalse(GitRefName.TryCreate(value, out GitRefName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	[DataRow("-f")]
	[DataRow("--upload-pack=calc.exe")]
	public void GitRemoteNameRejectsLeadingDash(string value)
	{
		Assert.IsFalse(GitRemoteName.TryCreate(value, out GitRemoteName? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	[DataRow("-f")]
	[DataRow("--upload-pack=calc.exe")]
	public void GitRepositoryRemotePathRejectsLeadingDash(string value)
	{
		Assert.IsFalse(GitRepositoryRemotePath.TryCreate(value, out GitRepositoryRemotePath? result));
		Assert.IsNull(result);
	}

	[TestMethod]
	public void LeadingDashRejectionDoesNotAffectOrdinaryValues()
	{
		Assert.IsTrue(GitBranchName.TryCreate("feature/git-v2", out GitBranchName? branch));
		Assert.IsNotNull(branch);
		Assert.IsTrue(GitRefName.TryCreate("HEAD~1", out GitRefName? reference));
		Assert.IsNotNull(reference);
		Assert.IsTrue(GitRemoteName.TryCreate("origin", out GitRemoteName? remote));
		Assert.IsNotNull(remote);
		Assert.IsTrue(GitRepositoryRemotePath.TryCreate("https://github.com/ktsu-dev/GitIntegration.git", out GitRepositoryRemotePath? remotePath));
		Assert.IsNotNull(remotePath);
	}

	[TestMethod]
	public void FindsTheLibrarysSemanticStringTypes()
	{
		// Guards the reflection below: if it found nothing, the distinctness test would pass vacuously.
		Type[] types = PublicSemanticStringTypes();

		Assert.Contains(typeof(GitBranchName), types);
		Assert.Contains(typeof(GitRemoteName), types);
		Assert.Contains(typeof(GitCommitSha), types);
		Assert.Contains(typeof(GitRepositoryName), types);
	}

	[TestMethod]
	public void SemanticTypesAreDistinctAtCompileTime()
	{
		// A semantic type is only worth having if the compiler refuses to accept one where another is
		// expected. Two things would quietly undo that: one type deriving from another, or an implicit
		// conversion between them, which leaves the runtime types distinct while making every call site
		// interchangeable. Every public semantic-string type in the library is checked against every
		// other, so a new type is covered without editing this test.
		Type[] types = PublicSemanticStringTypes();
		List<string> violations = [];

		foreach (Type from in types)
		{
			foreach (Type to in types)
			{
				if (from == to)
				{
					continue;
				}

				if (to.IsAssignableFrom(from))
				{
					violations.Add($"{from.Name} is assignable to {to.Name}");
				}

				if (DeclaresImplicitConversion(from, to))
				{
					violations.Add($"an implicit conversion turns {from.Name} into {to.Name}");
				}
			}
		}

		Assert.IsEmpty(violations, string.Join("; ", violations.Distinct()));
	}

	private static Type[] PublicSemanticStringTypes() =>
		[.. typeof(GitBranchName).Assembly.GetExportedTypes()
			.Where(type => !type.IsAbstract && IsSemanticString(type))
			.OrderBy(type => type.FullName, StringComparer.Ordinal)];

	private static bool IsSemanticString(Type type)
	{
		for (Type? current = type.BaseType; current is not null; current = current.BaseType)
		{
			if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(SemanticString<>))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Reports whether <paramref name="declaringType"/>, or a base it inherits operators from, declares
	/// an <c>op_Implicit</c> from <paramref name="from"/> to <paramref name="to"/>.
	/// </summary>
	private static bool DeclaresImplicitConversion(Type declaringType, Type from, Type to) =>
		declaringType
			.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
			.Any(method => method.Name == "op_Implicit"
				&& method.ReturnType == to
				&& method.GetParameters() is [{ } parameter]
				&& parameter.ParameterType == from);

	private static bool DeclaresImplicitConversion(Type from, Type to) =>
		DeclaresImplicitConversion(from, from, to) || DeclaresImplicitConversion(to, from, to);

	[TestMethod]
	public void GitRepositoryNameAcceptsAnOrdinarySingleSegmentName()
	{
		// The denylist must not become an allowlist. GitHub restricts names to A-Z a-z 0-9 . _ -,
		// Azure DevOps permits spaces and more, and both shapes have to keep working.
		Assert.AreEqual("GitIntegration", GitRepositoryName.Create("GitIntegration").WeakString);
		Assert.AreEqual("my.repo_v2-final", GitRepositoryName.Create("my.repo_v2-final").WeakString);
		Assert.AreEqual("Team Project Repo", GitRepositoryName.Create("Team Project Repo").WeakString);

		// A relative segment only means anything when a separator delimits it, and separators are
		// rejected outright, so dots inside a name are ordinary characters.
		Assert.AreEqual("my..repo", GitRepositoryName.Create("my..repo").WeakString);
	}

	[TestMethod]
	public void GitRepositoryNameRejectsAnythingThatWouldReshapeARequestPath()
	{
		// Both providers substitute this value straight into a request path and escape it
		// differently — Azure DevOps runs Uri.EscapeDataString over it, GitHub hands it to Octokit
		// unescaped — so a separator reshapes the request on one host and not the other. Rejecting at
		// construction closes both at the source.
		foreach (string candidate in new[]
		{
			"owner/repo",
			"..",
			".",
			"../../etc",
			"repo?query=1",
			"repo#fragment",
			@"owner\repo",
		})
		{
			_ = Assert.ThrowsExactly<ArgumentException>(
				() => _ = GitRepositoryName.Create(candidate),
				$"'{candidate}' should not be a valid repository name.");
		}
	}
}
