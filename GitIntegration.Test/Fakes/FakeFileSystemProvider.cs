// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitIntegration.Test;

using System.IO.Abstractions;

using ktsu.Essentials;

using Testably.Abstractions.Testing;

/// <summary>
/// Presents an in-memory <see cref="MockFileSystem"/> as an <see cref="IFileSystemProvider"/>.
/// </summary>
/// <remarks>
/// <see cref="IFileSystemProvider"/> is a marker interface over
/// <see cref="System.IO.Abstractions.IFileSystem"/> and adds no members of its own, so this is pure
/// delegation. It exists because <see cref="MockFileSystem"/> implements the base interface but not
/// the marker, and the library's constructors ask for the marker.
/// </remarks>
/// <param name="inner">The in-memory filesystem to delegate to.</param>
internal sealed class FakeFileSystemProvider(MockFileSystem inner) : IFileSystemProvider
{
	public IDirectory Directory => inner.Directory;

	public IDirectoryInfoFactory DirectoryInfo => inner.DirectoryInfo;

	public IDriveInfoFactory DriveInfo => inner.DriveInfo;

	public IFile File => inner.File;

	public IFileInfoFactory FileInfo => inner.FileInfo;

	public IFileStreamFactory FileStream => inner.FileStream;

	public IFileSystemWatcherFactory FileSystemWatcher => inner.FileSystemWatcher;

	public IFileVersionInfoFactory FileVersionInfo => inner.FileVersionInfo;

	public IPath Path => inner.Path;
}
