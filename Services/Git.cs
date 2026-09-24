using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PluginHub.Services;

/// <summary>Asks git about the root folder. Read-only: the hub never commits for you.</summary>
public static class Git
{
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
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            foreach (string a in new[] { "-c", "core.quotepath=off", "-C", root }.Concat(args))
                psi.ArgumentList.Add(a);
            // Looking must not take the index lock a commit in progress needs
            psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(15_000))
            {
                try { p.Kill(true); } catch { }
                return (-1, "");
            }
            return (p.ExitCode, output.Result);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return (-1, "");   // git is not installed
        }
    }
}

public sealed record GitChange(string State, string Path);

public sealed record Commit(string Hash, DateTimeOffset Date, string Subject);
