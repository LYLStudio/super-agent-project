using System.Text.Json;
using SuperAgent.Core;
using Xunit;

namespace SuperAgent.Core.Tests;

public class WorkspaceAccessPolicyTests
{
    [Fact]
    public void ResolvePath_AnchorsRelativePathsToWorkspace()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var policy = new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false);

            var resolved = policy.ResolvePath(Path.Combine("src", "Program.cs"));

            Assert.Equal(Path.Combine(workspace, "src", "Program.cs"), resolved);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void ResolvePath_RejectsPathsOutsideWorkspace()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var policy = new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false);

            Assert.Throws<UnauthorizedAccessException>(() =>
                policy.ResolvePath(Path.Combine(workspace, "..", "outside.txt")));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void ResolvePath_RejectsSymbolicLinksThatEscapeWorkspace()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var workspace = Directory.CreateTempSubdirectory().FullName;
        var outside = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(workspace, "external"), outside);
            var policy = new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false);

            Assert.Throws<UnauthorizedAccessException>(() =>
                policy.ResolvePath(Path.Combine("external", "secret.txt")));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveCommandWorkingDirectory_AllowsWorkspaceSubdirectoriesByDefault()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        var subdirectory = Directory.CreateDirectory(Path.Combine(workspace, "src")).FullName;
        try
        {
            var policy = new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false);

            var resolved = await policy.ResolveCommandWorkingDirectoryAsync(
                "src",
                OutsideWorkspaceCommandAccess.Deny,
                confirmOutsideWorkspace: null,
                CancellationToken.None);

            Assert.Equal(subdirectory, resolved);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveCommandWorkingDirectory_DeniesOutsideWorkspaceByDefault()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        var outside = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var policy = new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                policy.ResolveCommandWorkingDirectoryAsync(
                    outside,
                    OutsideWorkspaceCommandAccess.Deny,
                    confirmOutsideWorkspace: null,
                    CancellationToken.None));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveCommandWorkingDirectory_RequiresConfirmationForOutsideWorkspace()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        var outside = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var policy = new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false);
            string? confirmedPath = null;

            var resolved = await policy.ResolveCommandWorkingDirectoryAsync(
                outside,
                OutsideWorkspaceCommandAccess.Confirm,
                (path, _) =>
                {
                    confirmedPath = path;
                    return Task.FromResult(true);
                },
                CancellationToken.None);

            Assert.Equal(outside, confirmedPath);
            Assert.Equal(outside, resolved);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                policy.ResolveCommandWorkingDirectoryAsync(
                    outside,
                    OutsideWorkspaceCommandAccess.Confirm,
                    (_, _) => Task.FromResult(false),
                    CancellationToken.None));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveCommandWorkingDirectory_AllowsOutsideWorkspaceWhenConfigured()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        var outside = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var policy = new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false);

            var resolved = await policy.ResolveCommandWorkingDirectoryAsync(
                outside,
                OutsideWorkspaceCommandAccess.Allow,
                confirmOutsideWorkspace: null,
                CancellationToken.None);

            Assert.Equal(outside, resolved);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task CodeWriter_RejectsWritesOutsideWorkspace()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        var outsidePath = Path.Combine(Directory.GetParent(workspace)!.FullName, $"{Guid.NewGuid():N}.txt");
        try
        {
            var tool = new CodeWriterTool(new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false));
            var arguments = JsonSerializer.SerializeToElement(new
            {
                file_path = outsidePath,
                content = "outside"
            });

            var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

            Assert.Contains("Access outside the configured workspace is disabled", result);
            Assert.False(File.Exists(outsidePath));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task CodeWriter_WritesRelativePathInsideWorkspace()
    {
        var workspace = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var tool = new CodeWriterTool(new WorkspaceAccessPolicy(workspace, allowOutsideWorkspace: false));
            var arguments = JsonSerializer.SerializeToElement(new
            {
                file_path = Path.Combine("src", "Program.cs"),
                content = "inside"
            });

            var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

            Assert.Contains("Successfully wrote", result);
            Assert.Equal(
                "inside",
                await File.ReadAllTextAsync(Path.Combine(workspace, "src", "Program.cs")));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }
}
