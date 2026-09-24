using PluginHub.Services;
using RhinoKit.Licensing;

namespace PluginHub.Api;

/// <summary>The hub's JSON interface - everything the page does goes through here.</summary>
public static class HubApi
{
    public static void MapHubApi(this WebApplication app)
    {
        // ── The hub itself ────────────────────────────────────────────────────
        app.MapGet("/api/hello", (HubPaths paths) => new { app = "PluginHub", root = paths.Root });
        app.MapPost("/api/heartbeat", () => Results.NoContent());
        app.MapPost("/api/quit", (IHostApplicationLifetime lifetime) =>
        {
            // Answer first, then stop
            _ = Task.Delay(300).ContinueWith(_ => lifetime.StopApplication());
            return Results.NoContent();
        });

        // ── Overview, projects, files ─────────────────────────────────────────
        app.MapGet("/api/overview", (Status status) => status.Overview());
        app.MapGet("/api/projects/{id}", (string id, Status status) => status.ProjectDetail(id));
        app.MapPost("/api/open", (OpenRequest request, Config config, HubPaths paths) =>
        {
            Shell.Open(paths, config.Link(request.LinkId));
            return Results.NoContent();
        });
        app.MapPost("/api/rhino/toolbar-cache/clear", (ProjectRequest request, Config config, Status status) =>
        {
            int removed = RhinoInfo.ClearToolbarCache(config.Project(request.Project));
            status.Invalidate();
            return new { removed };
        });

        // ── Builds ────────────────────────────────────────────────────────────
        app.MapPost("/api/builds", (BuildRequest request, Builds builds, Status status) =>
        {
            var job = builds.Start(request.Projects);
            status.Invalidate();
            return job.View(0);
        });
        app.MapGet("/api/builds/current", (int? from, Builds builds, Status status) =>
        {
            var job = builds.Current;
            if (job == null) return Results.NoContent();
            if (job.IsFinished) status.Invalidate();
            return Results.Ok(job.View(from ?? 0));
        });

        // ── Keys ──────────────────────────────────────────────────────────────
        app.MapGet("/api/this-machine", () => new { machineId = MachineId.ForUser() });
        app.MapPost("/api/keys/{product}/generate",
            (string product, GenerateKeyRequest request, Config config, KeyStore keys, HubPaths paths, Status status) =>
        {
            var project = config.Project(product);
            if (!project.IsPlugin || project.KeyFile == null)
                throw new HubError($"{project.Name} braucht keinen Schlüssel.");

            // Replacing a key makes every code made with it worthless - the name is the proof
            bool replaces = keys.Info(project.Id) != null || PluginKeyFile.Read(paths, project) != null;
            if (replaces && !string.Equals(request.Confirm?.Trim(), project.Name, StringComparison.Ordinal))
                throw new HubError($"Ein neuer Schlüssel macht alle bisherigen Codes für {project.Name} ungültig. " +
                                   $"Tipp zur Bestätigung „{project.Name}“ ein.");

            var info = keys.Generate(project.Id);
            PluginKeyFile.Write(paths, project, info.PublicKey);
            Log.Info($"New key pair for {project.Id}: {info.Fingerprint}");
            status.Invalidate();
            return info;
        });

        // ── Register and codes ────────────────────────────────────────────────
        app.MapGet("/api/register", (Register register) => register.Snapshot());
        app.MapGet("/api/register.csv", (Register register, Config config) =>
            Results.File(Register.Csv(register.Snapshot(), config.Current), "text/csv",
                $"Kundenregister {DateTime.Now:yyyy-MM-dd}.csv"));

        app.MapPost("/api/customers", (CustomerRequest request, Register register, Status status) =>
        {
            var customer = register.AddCustomer(request.Name, request.Email, request.Note);
            status.Invalidate();
            return customer;
        });
        app.MapPut("/api/customers/{id}", (string id, CustomerRequest request, Register register, Status status) =>
        {
            var customer = register.UpdateCustomer(id, request.Name, request.Email, request.Note);
            status.Invalidate();
            return customer;
        });
        app.MapDelete("/api/customers/{id}", (string id, bool? licenses, Register register, Status status) =>
        {
            register.DeleteCustomer(id, licenses == true);
            status.Invalidate();
            return Results.NoContent();
        });

        app.MapPost("/api/licenses", (LicenseRequest request, Licenses licenses, Status status) =>
        {
            var result = licenses.Issue(request);
            status.Invalidate();
            return result;
        });
        app.MapPut("/api/licenses/{id}", (string id, LicenseUpdate request, Register register, Status status) =>
        {
            var license = register.UpdateLicense(id, Prices.Parse(request.Price), request.Paid, request.Note);
            status.Invalidate();
            return license;
        });
        app.MapDelete("/api/licenses/{id}", (string id, Register register, Status status) =>
        {
            register.DeleteLicense(id);
            status.Invalidate();
            return Results.NoContent();
        });

        // ── Backup ────────────────────────────────────────────────────────────
        app.MapPost("/api/backup", (BackupRequest request, Backup backup, Status status) =>
        {
            var record = backup.Create(request.Password);
            status.Invalidate();
            return record;
        });
        app.MapPost("/api/backup/restore", (RestoreRequest request, Backup backup, Status status) =>
        {
            var result = backup.Restore(request.Content, request.Password);
            status.Invalidate();
            return result;
        });
    }
}

public sealed record OpenRequest(string LinkId);

public sealed record ProjectRequest(string Project);

public sealed record BuildRequest(List<string> Projects);

public sealed record GenerateKeyRequest(string? Confirm);

public sealed record CustomerRequest(string Name, string? Email, string? Note);

public sealed record LicenseUpdate(string? Price, bool Paid, string? Note);

public sealed record BackupRequest(string? Password);

public sealed record RestoreRequest(string? Content, string? Password);
