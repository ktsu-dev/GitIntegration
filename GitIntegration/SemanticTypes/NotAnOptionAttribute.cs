// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration;

using System;

using ktsu.Semantics.Strings;

/// <summary>
/// Validates that a value does not begin with <c>-</c>, so it cannot be reinterpreted by git as an
/// option rather than an operand.
/// </summary>
/// <remarks>
/// Shell injection is already closed off by passing an argument vector rather than a command
/// string, but that does nothing about option injection: git itself parses each element, so a
/// branch, ref, remote, or remote path beginning with a dash is read as a flag. A remote path of
/// <c>--upload-pack=...</c> handed to <c>git clone</c> is arbitrary code execution. This attribute
/// rejects such values at construction, and <c>GitCommandBuilder&lt;TResult&gt;.AppendOperands</c>
/// provides the second layer by emitting <c>--end-of-options</c> before caller-supplied operands.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NotAnOptionAttribute : SemanticStringValidationAttribute
{
	/// <summary>
	/// Validates that the supplied value does not begin with a dash.
	/// </summary>
	/// <param name="semanticString">The semantic string to validate.</param>
	/// <returns>
	/// <see langword="true"/> when the value is non-empty and its first character is not
	/// <c>-</c>; otherwise, <see langword="false"/>.
	/// </returns>
	public override bool Validate(ISemanticString semanticString)
	{
		string? value = semanticString?.WeakString;

		return !string.IsNullOrEmpty(value) && value[0] != '-';
	}
}
