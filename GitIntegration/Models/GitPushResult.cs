// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// What a push did to each reference it touched.
/// </summary>
public sealed record GitPushResult
{
	/// <summary>Gets the references the push touched, in the order git listed them.</summary>
	public required IReadOnlyList<GitRefUpdate> Updates { get; init; }

	/// <summary>
	/// Gets a value indicating whether git refused any of the updates.
	/// </summary>
	/// <remarks>
	/// A rejected push exits non-zero while still reporting a complete account of every reference,
	/// so this can be true on a result the caller obtained without an exception — see
	/// <c>IGitPushBuilder</c>, where <c>ExecuteAsync</c> and <c>TryExecuteAsync</c> deliberately
	/// treat rejection differently.
	/// </remarks>
	public bool HasRejections => Updates.Any(static update => update.IsRejected);
}
