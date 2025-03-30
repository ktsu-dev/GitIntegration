namespace ktsu.GitIntegration;

using ktsu.StrongPaths;
using ktsu.StrongStrings;

/// <summary>
/// Represents a strongly-typed name for a Git repository.
/// </summary>
public sealed record class GitRepositoryName : StrongStringAbstract<GitRepositoryName> { }
/// <summary>
/// Represents a strongly-typed web URI for a Git repository.
/// </summary>
public sealed record class GitRepositoryWebURI : StrongStringAbstract<GitRepositoryWebURI> { }
/// <summary>
/// Represents a strongly-typed remote path for a Git repository.
/// </summary>
public sealed record class GitRepositoryRemotePath : StrongStringAbstract<GitRepositoryRemotePath> { }

/// <summary>
/// Represents a Git repository with its associated metadata and functionality.
/// </summary>
public class GitRepository
{
	/// <summary>
	/// Gets the name of the Git repository.
	/// </summary>
	public GitRepositoryName Name { get; init; } = new();

	/// <summary>
	/// Gets the web URI for accessing the Git repository through a browser.
	/// </summary>
	public GitRepositoryWebURI WebURI { get; init; } = new();

	/// <summary>
	/// Gets the remote path of the Git repository.
	/// </summary>
	public GitRepositoryRemotePath RemotePath { get; init; } = new();

	/// <summary>
	/// Gets the local file system path where the Git repository is or will be cloned.
	/// </summary>
	public AbsoluteDirectoryPath LocalPath { get; init; } = new();

	/// <summary>
	/// Gets a value indicating whether the repository has been cloned locally.
	/// </summary>
	/// <remarks>
	/// Currently only checks if the directory exists. Future implementations should verify it's a valid Git repository.
	/// </remarks>
	public bool IsCloned => Directory.Exists(LocalPath); //TODO: Implement this properly by checking if it is a valid clone

	/// <summary>
	/// Opens the repository in the default web browser.
	/// </summary>
	public void OpenWebClient()
	{
		_ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
		{
			FileName = "explorer",
			Arguments = WebURI,
			UseShellExecute = true,
			Verb = "open",
		});
	}
}
