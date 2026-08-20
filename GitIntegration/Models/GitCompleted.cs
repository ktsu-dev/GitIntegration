// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;

/// <summary>
/// The result of a git command whose only outcome is that it succeeded.
/// </summary>
/// <remarks>
/// Several mutating verbs — <c>add</c>, <c>checkout</c>, branch creation and deletion, and the
/// remote commands — write nothing to standard output that is worth reading, and report failure
/// through the exit code alone. C# has no generic <c>void</c>, so
/// <see cref="IGitCommandBuilder{TResult}"/> needs a type to close over; this is it. The argument
/// vector is carried because it is the one piece of information a caller might still want after a
/// successful run, for logging or for reproducing the command by hand.
/// </remarks>
public sealed record GitCompleted
{
	/// <summary>Gets the argument vector git was run with.</summary>
	public required IReadOnlyList<string> Arguments { get; init; }
}
