namespace ktsu.GitIntegration;

using ktsu.StrongPaths;
using ktsu.StrongStrings;

public sealed record class GitRepositoryName : StrongStringAbstract<GitRepositoryName> { }
public sealed record class GitRepositoryWebURI : StrongStringAbstract<GitRepositoryWebURI> { }
public sealed record class GitRepositoryRemotePath : StrongStringAbstract<GitRepositoryRemotePath> { }

public class GitRepository
{
	public GitRepositoryName Name { get; init; } = new();
	public GitRepositoryWebURI WebURI { get; init; } = new();
	public GitRepositoryRemotePath RemotePath { get; init; } = new();
	public AbsoluteDirectoryPath LocalPath { get; init; } = new();

	public bool IsCloned => Directory.Exists(LocalPath); //TODO: Implement this properly by checking if it is a valid clone

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
