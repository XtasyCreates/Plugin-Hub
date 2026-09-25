using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PluginHub.Services;

/// <summary>
/// Asks git about the root folder. It changes something only on a click: a release's commits and
/// tag, "Hub-Änderungen committen", and uploading to the backup repository.
/// </summary>
public static class Git
{
    /// <summary>A git command that must succeed; its error text becomes the message.</summary>
    public static string Exec(string root, params string[] args)
    {
        var (code, output, error) = RunAll(root, args);
        if (code != 0)
            throw new HubError($"git {args.FirstOrDefault()} ist fehlgeschlagen: {(error.Trim().Length > 0 ? error.Trim() : output.Trim())}");
        return output;
    }

    public static bool TagExists(string root, string tag) =>
        Run(root, "rev-parse", "-q", "--verify", "refs/tags/" + tag).Code == 0;

    public static string Head(string root) => Run(root, "rev-parse", "--short", "HEAD").Output.Trim();

    /// <summary>Commit subjects between a tag and HEAD that touch these folders, oldest first.</summary>
    public static List<string> SubjectsSince(string root, string? tag, IEnumerable<string> folders)
    {
        var args = new List<string> { "log", "--reverse", "--format=%s" };
        if (tag != null) args.Add(tag + "..HEAD");
        args.Add("--");
        args.AddRange(folders);
        var (code, output) = Run(root, args.ToArray());
        return code == 0 ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd('\r')).ToList() : new();
    }
    public static bool IsRepo(string root) =>
        Run(root, "rev-parse", "--is-inside-work-tree").Output.Trim() == "true";

    /// <summary>Everything not committed - changed, new and untracked - relative to the root.</summary>
    public static List<GitChange> Changes(string root)
    {
        var (code, output) = Run(root, "status", "--porcelain=v1", "-z", "--untracked-files=normal");
        var list = new List<GitChange>();
        if (code != 0) return list;

        var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            string entry = entries[i];
            if (entry.Length < 4) continue;
            list.Add(new GitChange(entry[..2], entry[3..]));
            if (entry[0] is 'R' or 'C') i++;   // a rename is followed by the old name
        }
        return list;
    }

    public static string? LatestTag(string root, string pattern)
    {
        var (code, output) = Run(root, "describe", "--tags", "--abbrev=0", "--match", pattern);
        return code == 0 && output.Trim().Length > 0 ? output.Trim() : null;
    }

    /// <summary>One line per tag: name, the object it points at, and for annotated tags the commit.</summary>
    public static string TagRefs(string root) =>
        Run(root, "for-each-ref", "refs/tags", "--format=%(refname:short) %(objectname) %(*objectname)").Output;

    /// <summary>Every commit of the branch, oldest first, with the files it touched.</summary>
    public static List<GitCommit> History(string root)
    {
        var (code, output) = Run(root, "log", "--reverse", "--format=%x1e%H%x1f%cI%x1f%s", "--name-only");
        var list = new List<GitCommit>();
        if (code != 0) return list;
        foreach (string chunk in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = chunk.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            var head = lines[0].Split('\x1f');
            if (head.Length != 3 || !DateTimeOffset.TryParse(head[1], out var date)) continue;
            list.Add(new GitCommit(head[0], date, head[2], lines.Skip(1).Where(l => l.Length > 0).ToList()));
        }
        return list;
    }

    public static string? RemoteUrl(string root, string remote)
    {
        var (code, output) = Run(root, "remote", "get-url", remote);
        return code == 0 && output.Trim().Length > 0 ? output.Trim() : null;
    }

    public static string Branch(string root) => Run(root, "rev-parse", "--abbrev-ref", "HEAD").Output.Trim();

    /// <summary>Commits on HEAD that the remote's copy of the branch does not have - all of them before the first push.</summary>
    public static int Ahead(string root, string remote, string branch)
    {
        bool known = Run(root, "rev-parse", "-q", "--verify", $"refs/remotes/{remote}/{branch}").Code == 0;
        var (code, output) = Run(root, "rev-list", "--count", known ? $"{remote}/{branch}..HEAD" : "HEAD");
        return code == 0 && int.TryParse(output.Trim(), out int n) ? n : 0;
    }

    public static List<string> Tags(string root)
    {
        var (code, output) = Run(root, "tag", "--list");
        return code == 0 ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList() : new();
    }

    /// <summary>
    /// A git command whose output lines go to a job as they come - a push, which may wait for
    /// the sign-in window of Git's credential manager. Returns the exit code.
    /// </summary>
    public static int Stream(string root, BuildJob job, TimeSpan timeout, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };
        foreach (string a in new[] { "-C", root }.Concat(args)) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) job.Add(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) job.Add(e.Data); };
        try { p.Start(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new HubError("git ist nicht installiert.");
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(true); } catch { }
            job.Add($"Abgebrochen: länger als {timeout.TotalMinutes:0} Minuten.");
            return -1;
        }
        p.WaitForExit();
        return p.ExitCode;
    }

    public static List<Commit> Log(string root, string? folder, int count)
    {
        var args = new List<string> { "log", $"-n{count}", "--format=%h%x1f%cI%x1f%s" };
        if (folder != null) args.AddRange(new[] { "--", folder });

        var (code, output) = Run(root, args.ToArray());
        var list = new List<Commit>();
        if (code != 0) return list;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\x1f');
            if (parts.Length == 3 && DateTimeOffset.TryParse(parts[1], out var date))
                list.Add(new Commit(parts[0], date, parts[2].TrimEnd('\r')));
        }
        return list;
    }

    private static (int Code, string Output) Run(string root, params string[] args)
    {
        var (code, output, _) = RunAll(root, args);
        return (code, output);
    }

    private static (int Code, string Output, string Error) RunAll(string root, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            };
            foreach (string a in new[] { "-c", "core.quotepath=off", "-C", root }.Concat(args))
                psi.ArgumentList.Add(a);
            // Looking must not take the index lock a commit in progress needs
            psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error  = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30_000))
            {
                try { p.Kill(true); } catch { }
                return (-1, "", "git hat nicht geantwortet.");
            }
            return (p.ExitCode, output.Result, error.Result);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return (-1, "", "git ist nicht installiert.");
        }
    }
}

public sealed record GitChange(string State, string Path);

public sealed record Commit(string Hash, DateTimeOffset Date, string Subject);

public sealed record GitCommit(string Hash, DateTimeOffset Date, string Subject, List<string> Files);
