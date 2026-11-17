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
        // The page asks every half minute - and hears whether new code requests came in
        app.MapPost("/api/heartbeat", (Inbox inbox) => new { requests = inbox.NewCount });
        app.MapPost("/api/quit", (IHostApplicationLifetime lifetime) =>
        {
            // Answer first, then stop
            _ = Task.Delay(300).ContinueWith(_ => lifetime.StopApplication());
            return Results.NoContent();
        });

        // ── The phone ─────────────────────────────────────────────────────────
        // Where the page is open: on the phone it leaves out what only works on this PC
        app.MapGet("/api/session", (HttpContext context, PhoneAccess phone) =>
            new { phone = LocalOnly.FromPhone(context), phoneAccess = phone.Enabled });
        app.MapGet("/api/phone", (PhoneAccess phone) => phone.View());
        app.MapPost("/api/phone/on", (PhoneAccess phone, Status status) =>
        {
            var view = phone.TurnOn();
            status.Invalidate();
            return view;
        });
        app.MapPost("/api/phone/off", (PhoneAccess phone, Status status) =>
        {
            var view = phone.TurnOff();
            status.Invalidate();
            return view;
        });
        // Tailscale connected from the Windows start on, before sign-in ("Run unattended")
        app.MapPost("/api/phone/unattended", (UnattendedRequest request, PhoneAccess phone, Status status) =>
        {
            var view = phone.SetUnattended(request.On);
            status.Invalidate();
            return view;
        });

        // ── Overview, projects, files ─────────────────────────────────────────
        app.MapGet("/api/overview", (Status status) => status.Overview());
        app.MapGet("/api/stats", (Stats stats) => stats.View());
        // The milestones on the overview. Every change answers with the chain as it is now, and
        // touches a file in Git - hence the fresh overview.
        app.MapGet("/api/milestones", (Milestones milestones) => milestones.View());
        app.MapPost("/api/milestones", (MilestoneRequest request, Milestones milestones, Status status) =>
        {
            var view = milestones.Add(request);
            status.Invalidate();
            return view;
        });
        app.MapPost("/api/milestones/order", (MilestoneOrderRequest request, Milestones milestones, Status status) =>
        {
            var view = milestones.Reorder(request.Ids);
            status.Invalidate();
            return view;
        });
        app.MapPut("/api/milestones/{id}", (string id, MilestoneRequest request, Milestones milestones, Status status) =>
        {
            var view = milestones.Update(id, request);
            status.Invalidate();
            return view;
        });
        app.MapDelete("/api/milestones/{id}", (string id, Milestones milestones, Status status) =>
        {
            var view = milestones.Delete(id);
            status.Invalidate();
            return view;
        });
        app.MapPost("/api/milestones/{id}/hidden", (string id, MilestoneHiddenRequest request, Milestones milestones, Status status) =>
        {
            var view = milestones.Hide(id, request.Hidden);
            status.Invalidate();
            return view;
        });
        app.MapPost("/api/milestones/{id}/recount", (string id, MilestoneRecountRequest request, Milestones milestones, Status status) =>
        {
            var view = milestones.Recount(id, request.On);
            status.Invalidate();
            return view;
        });
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
        app.MapPost("/api/git/commit-hub", (Releases releases, Status status) =>
        {
            int files = releases.CommitHubFiles();
            status.Invalidate();
            return new { files };
        });

        // ── Builds and releases ───────────────────────────────────────────────
        app.MapPost("/api/builds", (BuildRequest request, Builds builds, Status status) =>
        {
            var job = builds.Start(request.Projects, request.Develop);
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
        // A copy of a project to hand on - a job like a build, followed the same way
        app.MapPost("/api/share/{id}", (string id, Share share) => share.Start(id).View(0));
        app.MapGet("/api/releases", (Config config, Releases releases) =>
            config.Current.Projects.Where(p => p.IsPlugin).Select(releases.State).ToList());
        app.MapGet("/api/releases/{id}/plan", (string id, Config config, Releases releases) =>
            releases.Plan(config.Project(id)));
        app.MapPost("/api/releases/{id}", (string id, ReleaseRequest request, Config config, Releases releases, Status status) =>
        {
            var job = releases.Start(config.Project(id), request);
            status.Invalidate();
            return job.View(0);
        });
        // Rhino packages (.yak): a probe of the current build, signing in to Rhino, uploading a release's
        app.MapPost("/api/releases/{id}/yak/probe", (string id, Config config, Releases releases) =>
            releases.Probe(config.Project(id)).View(0));
        app.MapPost("/api/releases/yak/signin", (Releases releases) => releases.SignIn().View(0));
        app.MapPost("/api/releases/{id}/yak/push", (string id, PushRequest request, Config config, Releases releases, Status status) =>
        {
            var job = releases.Push(config.Project(id), request.Version);
            status.Invalidate();
            return job.View(0);
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
            bool replaces = keys.Info(project.Id) != null || PluginActivationFile.ReadKey(paths, project) != null;
            bool suiteKey = project.Id == Suite.KeyProduct;
            if (replaces && !string.Equals(request.Confirm?.Trim(), project.Name, StringComparison.Ordinal))
                throw new HubError($"Ein neuer Schlüssel macht alle bisherigen Codes für {project.Name} ungültig" +
                                   (suiteKey ? " – und als Suite-Schlüssel auch jeden Code mit Produkten, für alle Plugins. " : ". ") +
                                   $"Tipp zur Bestätigung „{project.Name}“ ein.");

            var info = keys.Generate(project.Id);
            if (suiteKey)
            {
                // Laser Create's key is the suite key: every plug-in of the suite checks against it
                PluginActivationFile.WriteKey(paths, project, info.PublicKey, info.PublicKey);
                foreach (var other in config.Current.Projects.Where(p => p.IsPlugin && p.KeyFile != null && p.Id != project.Id))
                    if (PluginActivationFile.ReadKey(paths, other) != null)
                        PluginActivationFile.WriteSuiteKey(paths, other, info.PublicKey);
            }
            else
            {
                PluginActivationFile.WriteKey(paths, project, info.PublicKey, keys.Info(Suite.KeyProduct)?.PublicKey);
            }
            Log.Info($"New key pair for {project.Id}: {info.Fingerprint}");
            status.Invalidate();
            return info;
        });

        // ── Register and codes ────────────────────────────────────────────────
        app.MapGet("/api/register", (Status status) => status.RegisterView());
        app.MapGet("/api/register.csv", (Register register, Config config) =>
            Results.File(Register.Csv(register.Snapshot(), config.Current), "text/csv",
                $"Kundenregister {DateTime.Now:yyyy-MM-dd}.csv"));

        app.MapPost("/api/customers", (CustomerRequest request, Register register, Status status) =>
        {
            var customer = register.AddCustomer(request.Fields);
            status.Invalidate();
            return customer;
        });
        app.MapPut("/api/customers/{id}", (string id, CustomerRequest request, Register register, Status status) =>
        {
            var customer = register.UpdateCustomer(id, request.Fields);
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
        app.MapPost("/api/licenses/{id}/extend", (string id, ExtendRequest request, Licenses licenses, Status status) =>
        {
            var result = licenses.Extend(id, request);
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

        // ── Mails ─────────────────────────────────────────────────────────────
        app.MapGet("/api/mails", (Mailer mailer, Settings settings, Status status) =>
            new { ready = settings.MailReady, due = mailer.Due(), sent = status.RegisterView().Mails.OrderByDescending(m => m.Sent).ToList() });
        app.MapPost("/api/mails/preview", (MailRequest request, Mailer mailer) =>
            mailer.Draft(request.Kind, request.LicenseId, request.Language));
        app.MapPost("/api/mails/send", (MailRequest request, Mailer mailer, Inbox inbox, Status status) =>
        {
            var record = mailer.Send(request.Kind, request.LicenseId, request.Subject ?? "", request.Body ?? "", request.InReplyTo);
            // The code is on its way: only now is the request it answers done
            if (request.Kind is "code" or "extension") inbox.Answered(request.InReplyTo, request.LicenseId);
            status.Invalidate();
            return record;
        });
        app.MapPost("/api/mails/skip", (MailRequest request, Register register, Status status) =>
        {
            register.MarkFollowUp(request.LicenseId, skipped: true);
            status.Invalidate();
            return Results.NoContent();
        });
        app.MapPost("/api/mails/test", (Mailer mailer) => new { to = mailer.SendTest() });
        app.MapGet("/api/templates/mails", (Mailer mailer) => mailer.Templates);
        app.MapPut("/api/templates/mails", (Dictionary<string, Dictionary<string, MailTemplate>> templates, Mailer mailer, Status status) =>
        {
            mailer.SaveTemplates(templates);
            status.Invalidate();
            return Results.NoContent();
        });

        // ── Settings ──────────────────────────────────────────────────────────
        app.MapGet("/api/settings", (Settings settings) =>
            new { shared = settings.Shared, mail = settings.Mail, hasPassword = settings.HasPassword, ready = settings.MailReady });
        app.MapPut("/api/settings/shared", (HubSettings request, Settings settings, Status status) =>
        {
            var saved = settings.SaveShared(request);
            status.Invalidate();
            return saved;
        });
        app.MapPut("/api/settings/mail", (MailSettings request, Settings settings, Status status) =>
        {
            var saved = settings.SaveMail(request);
            status.Invalidate();
            return saved;
        });
        app.MapPut("/api/settings/mail-password", (PasswordRequest request, Settings settings, Status status) =>
        {
            settings.SetPassword(request.Password);
            status.Invalidate();
            return Results.NoContent();
        });

        // ── Tasks ─────────────────────────────────────────────────────────────
        app.MapGet("/api/tasks", (Tasks tasks) => tasks.All());
        app.MapPost("/api/tasks", (TaskRequest request, Tasks tasks, Status status) =>
        {
            var task = tasks.Add(request);
            status.Invalidate();
            return task;
        });
        app.MapPut("/api/tasks/{id}", (string id, TaskRequest request, Tasks tasks, Status status) =>
        {
            var task = tasks.Update(id, request);
            status.Invalidate();
            return task;
        });
        app.MapPost("/api/tasks/{id}/done", (string id, DoneRequest request, Tasks tasks, Status status) =>
        {
            var task = tasks.SetDone(id, request.Done);
            status.Invalidate();
            return task;
        });
        app.MapDelete("/api/tasks/{id}", (string id, Tasks tasks, Status status) =>
        {
            tasks.Delete(id);
            status.Invalidate();
            return Results.NoContent();
        });

        // ── Code requests from the mailbox ────────────────────────────────────
        app.MapGet("/api/inbox", (Inbox inbox, Status status) => inbox.View(status.RegisterView()));
        app.MapPost("/api/inbox/check", (Inbox inbox, Status status) =>
        {
            int added = inbox.Check();
            status.Invalidate();
            return new { added };
        });
        app.MapPost("/api/inbox/{id}/status", (string id, InboxStatusRequest request, Inbox inbox, Status status) =>
        {
            var handled = inbox.Handle(id, request.Status, request.LicenseId);
            status.Invalidate();
            return handled;
        });

        // ── Planning: roadmap, decisions, versions, documents ─────────────────
        app.MapGet("/api/plan", (Plan plan) => new { roadmap = plan.Roadmap(), decisions = plan.Decisions() });
        app.MapPost("/api/roadmap", (RoadmapRequest request, Plan plan, Status status) =>
        {
            var item = plan.AddItem(request);
            status.Invalidate();
            return item;
        });
        app.MapPut("/api/roadmap/{id}", (string id, RoadmapRequest request, Plan plan, Status status) =>
        {
            var item = plan.UpdateItem(id, request);
            status.Invalidate();
            return item;
        });
        app.MapPost("/api/roadmap/{id}/move", (string id, MoveRequest request, Plan plan, Status status) =>
        {
            var item = plan.Move(id, request.Stage, request.Before, request.Reason);
            status.Invalidate();
            return item;
        });
        app.MapPost("/api/roadmap/{id}/tests/{index:int}", (string id, int index, DoneRequest request, Plan plan, Status status) =>
        {
            var item = plan.SetTest(id, index, request.Done);
            status.Invalidate();
            return item;
        });
        app.MapDelete("/api/roadmap/{id}", (string id, Plan plan, Status status) =>
        {
            plan.DeleteItem(id);
            status.Invalidate();
            return Results.NoContent();
        });
        app.MapPost("/api/decisions", (DecisionRequest request, Plan plan, Status status) =>
        {
            var item = plan.AddDecision(request);
            status.Invalidate();
            return item;
        });
        app.MapPut("/api/decisions/{id}", (string id, DecisionRequest request, Plan plan, Status status) =>
        {
            var item = plan.UpdateDecision(id, request);
            status.Invalidate();
            return item;
        });
        app.MapDelete("/api/decisions/{id}", (string id, Plan plan, Status status) =>
        {
            plan.DeleteDecision(id);
            status.Invalidate();
            return Results.NoContent();
        });
        app.MapGet("/api/versions", (Versions versions) => versions.All());
        app.MapGet("/api/docs", (Documents docs) => docs.List());
        app.MapGet("/api/docs/content", (string path, Documents docs) => docs.Read(path));
        app.MapPut("/api/docs/content", (DocSaveRequest request, Documents docs, Status status) =>
        {
            var saved = docs.Write(request.Path, request.Text, request.Stamp);
            status.Invalidate();
            return saved;
        });
        app.MapGet("/api/places", (Places places) => places.All());

        // ── The copies of the repositories outside this PC ────────────────────
        app.MapGet("/api/git/remote", (RepoBackup repo) => repo.State());
        app.MapPut("/api/git/remote", (RemoteRequest request, RepoBackup repo, Status status) =>
        {
            var state = repo.Connect(request.Repo, request.Url);
            status.Invalidate();
            return state;
        });
        app.MapPost("/api/git/push", (RepoBackup repo, Status status) =>
        {
            var job = repo.Push();
            status.Invalidate();
            return job.View(0);
        });
        // A project of its own in a repository of its own (MilkDrop): one commit of everything
        // open, and the upload of that repository alone - the button on its page does both
        app.MapPost("/api/git/commit-project", (CommitRequest request, Config config, Repos repos, Status status) =>
        {
            int changes = repos.CommitProject(config.Project(request.Project), request.Message);
            status.Invalidate();
            return new { changes };
        });
        app.MapPost("/api/git/push/{id}", (string id, Config config, RepoBackup repo, Status status) =>
        {
            var job = repo.Push(config.Project(id));
            status.Invalidate();
            return job.View(0);
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

public sealed record UnattendedRequest(bool On);

public sealed record PushRequest(string Version);

public sealed record ProjectRequest(string Project);

public sealed record CommitRequest(string Project, string? Message);

/// <summary>Develop: the developer build, with the plug-in's own developer tool (LaserDevelop).</summary>
public sealed record BuildRequest(List<string> Projects, bool Develop = false);

public sealed record GenerateKeyRequest(string? Confirm);

public sealed record CustomerRequest(string Name, string? Email, string? Note, string? Language, bool? MailConsent)
{
    public CustomerFields Fields => new(Name, Email, Note, Language, MailConsent ?? true);
}

public sealed record LicenseUpdate(string? Price, bool Paid, string? Note);

/// <summary>InReplyTo: the message id of a customer's request the mail answers.</summary>
public sealed record MailRequest(string Kind, string LicenseId, string? Language, string? Subject, string? Body, string? InReplyTo = null);

public sealed record PasswordRequest(string? Password);

public sealed record DoneRequest(bool Done);

public sealed record BackupRequest(string? Password);

public sealed record RestoreRequest(string? Content, string? Password);
