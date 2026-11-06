namespace SuperAgent.Core;

public enum OutsideWorkspaceCommandAccess
{
    Deny,
    Confirm,
    Allow
}

public sealed class WorkspaceAccessPolicy
{
    private readonly StringComparison _pathComparison;

    public WorkspaceAccessPolicy(string workspaceRoot, bool allowOutsideWorkspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(WorkspaceRoot))
        {
            throw new DirectoryNotFoundException($"Workspace directory not found: {WorkspaceRoot}");
        }

        var rootInfo = new DirectoryInfo(WorkspaceRoot);
        WorkspaceRoot = rootInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? rootInfo.FullName;
        WorkspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(WorkspaceRoot));
        AllowOutsideWorkspace = allowOutsideWorkspace;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public string WorkspaceRoot { get; }

    public bool AllowOutsideWorkspace { get; }

    public string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path, WorkspaceRoot);
        if (AllowOutsideWorkspace)
        {
            return fullPath;
        }

        EnsureInsideWorkspace(fullPath);
        EnsureExistingLinksStayInsideWorkspace(fullPath);
        return fullPath;
    }

    public async Task<string> ResolveCommandWorkingDirectoryAsync(
        string? path,
        OutsideWorkspaceCommandAccess outsideWorkspaceAccess,
        Func<string, CancellationToken, Task<bool>>? confirmOutsideWorkspace,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(outsideWorkspaceAccess))
        {
            throw new ArgumentOutOfRangeException(nameof(outsideWorkspaceAccess));
        }

        var fullPath = string.IsNullOrWhiteSpace(path)
            ? WorkspaceRoot
            : Path.GetFullPath(path, WorkspaceRoot);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Command working directory not found: {fullPath}");
        }

        if (IsInsideWorkspace(fullPath) || outsideWorkspaceAccess == OutsideWorkspaceCommandAccess.Allow)
        {
            return fullPath;
        }

        if (outsideWorkspaceAccess == OutsideWorkspaceCommandAccess.Deny)
        {
            throw new UnauthorizedAccessException(
                $"Command working directory is outside the configured workspace: {fullPath}");
        }

        if (confirmOutsideWorkspace is null ||
            !await confirmOutsideWorkspace(fullPath, cancellationToken))
        {
            throw new UnauthorizedAccessException(
                $"Access to command working directory outside the workspace was not confirmed: {fullPath}");
        }

        return fullPath;
    }

    private bool IsInsideWorkspace(string path)
    {
        try
        {
            EnsureInsideWorkspace(Path.GetFullPath(path));
            EnsureExistingLinksStayInsideWorkspace(Path.GetFullPath(path));
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void EnsureInsideWorkspace(string path)
    {
        var relativePath = Path.GetRelativePath(WorkspaceRoot, path);
        if (relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", _pathComparison) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", _pathComparison) ||
            Path.IsPathRooted(relativePath))
        {
            throw new UnauthorizedAccessException(
                $"Access outside the configured workspace is disabled: {path}");
        }
    }

    private void EnsureExistingLinksStayInsideWorkspace(string path)
    {
        var currentPath = path;
        while (!string.IsNullOrEmpty(currentPath) &&
               !string.Equals(currentPath, WorkspaceRoot, _pathComparison))
        {
            FileSystemInfo? fileSystemInfo = Directory.Exists(currentPath)
                ? new DirectoryInfo(currentPath)
                : File.Exists(currentPath)
                    ? new FileInfo(currentPath)
                    : null;

            if (fileSystemInfo is not null &&
                (fileSystemInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                {
                    EnsureInsideWorkspace(Path.GetFullPath(target.FullName));
                }
            }

            var parent = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(parent) ||
                string.Equals(parent, currentPath, _pathComparison))
            {
                break;
            }

            currentPath = parent;
        }
    }

    public static WorkspaceAccessPolicy CreateUnrestrictedDefault()
    {
        return new WorkspaceAccessPolicy(Directory.GetCurrentDirectory(), allowOutsideWorkspace: true);
    }
}
