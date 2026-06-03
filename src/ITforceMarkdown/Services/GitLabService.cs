#if PRO
using System;
using System.IO;
using System.Threading.Tasks;
using ITforceMarkdown.Models;
using LibGit2Sharp;

namespace ITforceMarkdown.Services;

/// <summary>
/// Pro 版 GitLab 同步 — clone (首次) 或 pull (后续) 到本地 cache。
/// 整个 class 用 #if PRO 包住, Local 编译时不存在, 也不引用 LibGit2Sharp 程序集。
/// </summary>
public static class GitLabService
{
    public sealed record SyncResult(bool Success, string Message, int CommitsPulled = 0);

    /// <summary>
    /// Cache 目录已有 .git → fetch + pull;
    /// Cache 目录空 / 不存在 → clone。
    /// </summary>
    public static Task<SyncResult> SyncAsync(GitLabSettings s)
    {
        return Task.Run(() => SyncSync(s));
    }

    private static SyncResult SyncSync(GitLabSettings s)
    {
        if (!s.IsConfigured)
            return new SyncResult(false, "GitLab is not configured.");

        Directory.CreateDirectory(s.LocalCachePath);

        var creds = new UsernamePasswordCredentials
        {
            Username = string.IsNullOrEmpty(s.Username) ? "oauth2" : s.Username,
            Password = s.AccessToken,
        };

        bool isRepo = Repository.IsValid(s.LocalCachePath);

        try
        {
            if (!isRepo)
            {
                var co = new CloneOptions();
                co.FetchOptions.CredentialsProvider = (_, _, _) => creds;
                Repository.Clone(s.ProjectUrl, s.LocalCachePath, co);
                return new SyncResult(true, "Repository cloned.");
            }
            else
            {
                using var repo = new Repository(s.LocalCachePath);
                var sig = new Signature("ITforce Markdown", "noreply@itforce.local", DateTimeOffset.Now);

                var fetchOptions = new FetchOptions
                {
                    CredentialsProvider = (_, _, _) => creds,
                };

                var pullOptions = new PullOptions
                {
                    FetchOptions = fetchOptions,
                    MergeOptions = new MergeOptions
                    {
                        FastForwardStrategy = FastForwardStrategy.FastForwardOnly,
                    },
                };

                var before = repo.Head.Tip?.Sha;
                var result = Commands.Pull(repo, sig, pullOptions);
                var after = repo.Head.Tip?.Sha;

                if (result.Status == MergeStatus.UpToDate)
                    return new SyncResult(true, "Already up to date.");
                if (result.Status == MergeStatus.FastForward)
                {
                    var n = (before != null && after != null && before != after)
                        ? CountAhead(repo, before, after)
                        : 0;
                    return new SyncResult(true, $"Fast-forwarded ({n} commit(s)).", n);
                }
                if (result.Status == MergeStatus.NonFastForward)
                    return new SyncResult(false,
                        "Local cache diverged from remote. Delete the cache and re-clone, or resolve manually.");
                return new SyncResult(false, $"Pull returned status: {result.Status}");
            }
        }
        catch (LibGit2SharpException ex)
        {
            return new SyncResult(false, "Git error: " + ex.Message);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, "Error: " + ex.Message);
        }
    }

    private static int CountAhead(Repository repo, string fromSha, string toSha)
    {
        try
        {
            var filter = new CommitFilter
            {
                IncludeReachableFrom = toSha,
                ExcludeReachableFrom = fromSha,
            };
            var n = 0;
            foreach (var _ in repo.Commits.QueryBy(filter)) n++;
            return n;
        }
        catch { return 0; }
    }
}
#endif
