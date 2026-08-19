// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

[TestClass]
public class GitParseValuesTests
{
	[TestMethod]
	public void ToSemanticAcceptsAValidValue()
	{
		GitCommitSha sha = GitParseValues.ToSemantic<GitCommitSha>("ABCDEF12", "commit id");

		// GitCommitSha canonicalises to lowercase, so the conversion must go through Create,
		// not through a cast that would bypass MakeCanonical.
		Assert.AreEqual("abcdef12".As<GitCommitSha>(), sha);
	}

	[TestMethod]
	public void ToSemanticRejectsAValueThatFailsValidation()
	{
		GitParseException exception = Assert.ThrowsExactly<GitParseException>(
			() => GitParseValues.ToSemantic<GitCommitSha>("zzzz", "commit id"));

		// The message has to name both what was expected and what git actually said, because a
		// parse failure is only ever diagnosed from the message.
		StringAssert.Contains(exception.Message, "commit id");
		StringAssert.Contains(exception.Message, "zzzz");
	}

	[TestMethod]
	public void ToSemanticRejectsAnEmptyValue()
	{
		Assert.ThrowsExactly<GitParseException>(
			() => GitParseValues.ToSemantic<GitCommitSha>(string.Empty, "commit id"));
	}

	[TestMethod]
	public void ToRelativeFilePathCanonicalisesSeparators()
	{
		RelativeFilePath path = GitParseValues.ToRelativeFilePath("docs/plan.md");

		// Built through As<T> rather than hard-coded, because RelativeFilePath rewrites '/' to the
		// host separator: a literal "docs\\plan.md" would pass on Windows and fail on Linux.
		Assert.AreEqual("docs/plan.md".As<RelativeFilePath>(), path);
	}

	[TestMethod]
	public void ToRelativeFilePathKeepsSpacesAndNonAsciiCharacters()
	{
		RelativeFilePath path = GitParseValues.ToRelativeFilePath("dir with spaces/ünïcødé.txt");

		Assert.AreEqual("dir with spaces/ünïcødé.txt".As<RelativeFilePath>(), path);
	}

	[TestMethod]
	public void ToRelativeFilePathRejectsAnEmptyPath()
	{
		// RelativeFilePath.TryCreate accepts the empty string, so the guard has to be explicit or a
		// malformed record yields a silently blank path.
		Assert.ThrowsExactly<GitParseException>(() => GitParseValues.ToRelativeFilePath(string.Empty));
	}

	[TestMethod]
	public void ToRelativeFilePathReportsAPathItCannotRepresent()
	{
		// Git permits a newline in a path and -z transports it intact, but RelativeFilePath refuses
		// control characters on Windows. Failing loudly beats dropping the entry, which would make
		// GitStatus.IsClean lie. On Linux the value is representable and no exception is thrown, so
		// the assertion is conditional on the platform.
		string path = "weird" + (char)10 + "name.txt";

		if (RelativeFilePath.TryCreate(path, out RelativeFilePath? supported) && supported is not null)
		{
			Assert.AreEqual(supported, GitParseValues.ToRelativeFilePath(path));
			return;
		}

		GitParseException exception = Assert.ThrowsExactly<GitParseException>(
			() => GitParseValues.ToRelativeFilePath(path));

		StringAssert.Contains(exception.Message, "name.txt");
	}
}
