using CityWebsiteAuditDashboard.Contracts;
using CityWebsiteAuditDashboard.Data;
using CityWebsiteAuditDashboard.Models;
using CityWebsiteAuditDashboard.Services.AuthenticatedAuditing;
using Microsoft.EntityFrameworkCore;

namespace CityWebsiteAuditDashboard.Services.AuditAgent;

public sealed class AuditAgentStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditAgentStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Owner> _owners = new();

    private sealed class Owner
    {
        public bool Disconnected;
        public HashSet<int> Runs { get; } = new();
        public Dictionary<Guid, (string Fingerprint, int Result)> Receipts { get; } = new();
    }

    public AuditAgentStore(IServiceScopeFactory scopeFactory, ILogger<AuditAgentStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RegisterAsync(string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_owners.ContainsKey(connectionId)) _owners.Add(connectionId, new Owner());
        }
        finally { _gate.Release(); }
    }

    public async Task<int> SaveAsync(string connectionId, AuditWrite write)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_owners.TryGetValue(connectionId, out Owner? owner) || owner.Disconnected)
                throw new InvalidOperationException("This Agent connection is no longer registered.");
            if (write.Id == Guid.Empty) throw new ArgumentException("A write ID is required.");
            string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(AuditAgentProtocol.Serialize(write))));
            if (owner.Receipts.TryGetValue(write.Id, out var receipt))
            {
                if (receipt.Fingerprint != fingerprint) throw new InvalidOperationException("A write ID was reused with different data.");
                return receipt.Result;
            }
            if (owner.Receipts.Count >= 4096)
                throw new InvalidOperationException("Restart the Agent between audit sessions to clear its receipt limit.");
            if (write.Operation != "Create" && !owner.Runs.Contains(write.RunId))
                throw new InvalidOperationException("This run does not belong to this Agent connection.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            int result;
            switch (write.Operation)
            {
                case "Create":
                    if (string.IsNullOrWhiteSpace(write.ApplicationName) || write.ApplicationName.Length > 200 ||
                        write.StartingUrl.Length > 2048 ||
                        !Uri.TryCreate(write.StartingUrl, UriKind.Absolute, out Uri? url) ||
                        (url.Scheme != "http" && url.Scheme != "https") || !string.IsNullOrEmpty(url.UserInfo))
                        throw new ArgumentException("Invalid audit start request.");
                    result = await CreateAuditRunAsync(write.ApplicationName, write.StartingUrl,
                        DateTime.UtcNow, timeout.Token);
                    owner.Runs.Add(result);
                    break;
                case "Step":
                    if (write.Step is null || write.Step.StepNumber < 1)
                        throw new ArgumentException("A numbered audit step is required.");
                    await using (var scope = _scopeFactory.CreateAsyncScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        if (!await db.AuthenticatedAuditRuns.AnyAsync(x => x.Id == write.RunId && x.Status == "Running", timeout.Token))
                            throw new InvalidOperationException("The audit is no longer running.");
                    }
                    result = await SaveAuditStepAsync(write.RunId, write.Step, timeout.Token);
                    break;
                case "Complete":
                    await CompleteAuditRunAsync(write.RunId, write.LastStepId, write.MarkFinal);
                    result = write.RunId;
                    break;
                case "Interrupt":
                    await MarkRunAsInterruptedAsync(write.RunId, write.Error ?? "The Agent session ended unexpectedly.");
                    result = write.RunId;
                    break;
                case "Fail":
                    await MarkRunAsUnsuccessfulAsync(write.RunId,
                        write.Status == "Cancelled" ? "Cancelled" : "Failed", write.Error);
                    result = write.RunId;
                    break;
                default: throw new ArgumentException("Unknown persistence operation.");
            }
            owner.Receipts.Add(write.Id, (fingerprint, result));
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync(string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_owners.TryGetValue(connectionId, out var owner)) owner.Disconnected = true;
        }
        finally { _gate.Release(); }
        await RecoverDisconnectedAsync();
    }

    public async Task RecoverDisconnectedAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var pair in _owners.Where(x => x.Value.Disconnected).ToArray())
            {
                try
                {
                    foreach (int run in pair.Value.Runs)
                        await MarkRunAsInterruptedAsync(run,
                            "The workstation Agent disconnected. Saved results were retained. Start a new audit to continue.");
                    _owners.Remove(pair.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not record Agent disconnect. Recovery will retry.");
                }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<int> CreateAuditRunAsync(
        string applicationName,
        string startingUrl,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        // ApplicationDbContext is scoped. Because this service will live longer
        // than one web request, obtain a fresh scope for each database operation.
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();


        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();


        var auditRun = new AuthenticatedAuditRun
        {
            ApplicationName = applicationName,
            StartingUrl = startingUrl,
            AccessibilityEngine = "axe-core",
            StartedAt = startedAt,
            Status = "Running"
        };


        dbContext.AuthenticatedAuditRuns.Add(auditRun);
        await dbContext.SaveChangesAsync(cancellationToken);


        return auditRun.Id;
    }


    private async Task<int> SaveAuditStepAsync(
    int auditRunId,
    AuthenticatedAuditStepResult stepResult,
    CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope =
            _scopeFactory.CreateAsyncScope();


        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();


        var auditStep = new AuthenticatedAuditStep
        {
            AuthenticatedAuditRunId = auditRunId,
            StepNumber = stepResult.StepNumber,
            StepName = LimitLength(stepResult.StepName, 200)
                ?? $"Step {stepResult.StepNumber}",
            Url = LimitLength(stepResult.Url, 2048)
                ?? string.Empty,
            PageTitle = LimitLength(stepResult.PageTitle, 500),
            Heading = LimitLength(stepResult.Heading, 500),
            DomFingerprint = LimitLength(
                stepResult.DomFingerprint,
                128),
            ScannedAt = stepResult.ScannedAt,
            VisibleFormCount = stepResult.VisibleFormCount,
            VisibleFieldCount = stepResult.VisibleFieldCount,
            VisibleButtonCount = stepResult.VisibleButtonCount,
            ViolationRuleCount = stepResult.ViolationRuleCount,
            AffectedElementCount = stepResult.AffectedElementCount,
            NeedsReviewRuleCount = stepResult.NeedsReviewRuleCount,
            PassedRuleCount = stepResult.PassedRuleCount,
            ScanSucceeded = stepResult.ScanSucceeded,
            WasFinalStep = false,
            ErrorMessage = LimitLength(
                stepResult.ErrorMessage,
                4000)
        };
        /*
        * Add findings through the navigation collection before SaveChanges.
        * EF Core will insert the step first and automatically use its generated ID
        * as AuthenticatedAuditStepId for each related finding.
         */
        foreach (AuthenticatedAuditFindingResult findingResult
         in stepResult.Findings)
        {
            var auditFinding = new AuthenticatedAuditFinding
            {
                FindingType =
                    LimitLength(findingResult.FindingType, 50)
                    ?? "Unknown",


                RuleId =
                    LimitLength(findingResult.RuleId, 200)
                    ?? string.Empty,


                Impact =
                    LimitLength(findingResult.Impact, 50),


                Help =
                    LimitLength(findingResult.Help, 500),


                Description =
                    LimitLength(findingResult.Description, 2000),


                HelpUrl =
                    LimitLength(findingResult.HelpUrl, 2048),


                // Preserve WCAG metadata for filtering and prioritization.
                WcagTags =
                    LimitLength(findingResult.WcagTags, 1000)
                    ?? string.Empty,


                WcagLevel =
                    LimitLength(findingResult.WcagLevel, 10),


                AffectedElementCount =
                    findingResult.AffectedElementCount
            };


            foreach (AuthenticatedAuditFindingNodeResult nodeResult
                     in findingResult.Nodes)
            {
                auditFinding.Nodes.Add(
                    new AuthenticatedAuditFindingNode
                    {
                        Target =
                            LimitLength(nodeResult.Target, 2000)
                            ?? string.Empty,


                        Html =
                            LimitLength(nodeResult.Html, 10000),


                        FailureSummary =
                            LimitLength(nodeResult.FailureSummary, 4000),


                        ElementFixGuidance =
                            LimitLength(
                                    BuildElementFixGuidance(
                                    findingResult.RuleId),
                                4000)
                    });
            }


            auditStep.Findings.Add(auditFinding);
        }


        int? existingId = await dbContext.AuthenticatedAuditSteps
            .Where(x => x.AuthenticatedAuditRunId == auditRunId && x.StepNumber == stepResult.StepNumber)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (existingId.HasValue) return existingId.Value;
        dbContext.AuthenticatedAuditSteps.Add(auditStep);
        await dbContext.SaveChangesAsync(cancellationToken);


        return auditStep.Id;
    }


    private async Task CompleteAuditRunAsync(
    int auditRunId,
    int? lastSavedStepId,
    bool markLastStepAsFinal)
    {
        await using AsyncServiceScope scope =
            _scopeFactory.CreateAsyncScope();


        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();


        AuthenticatedAuditRun? auditRun =
            await dbContext.AuthenticatedAuditRuns.FindAsync(
                new object[] { auditRunId },
                CancellationToken.None);


        if (auditRun is null)
        {
            throw new InvalidOperationException(
                $"Authenticated audit run {auditRunId} could not be found.");
        }


        if (auditRun.Status == "Completed") return;
        if (auditRun.Status != "Running")
            throw new InvalidOperationException("This audit is no longer running.");

        if (markLastStepAsFinal && lastSavedStepId.HasValue)
        {
            AuthenticatedAuditStep? lastStep =
                await dbContext.AuthenticatedAuditSteps.FindAsync(
                    new object[] { lastSavedStepId.Value },
                    CancellationToken.None);


            /*
             * Verify that the step actually belongs to this run before changing it.
             * This protects against marking an unrelated database record as final.
             */
            if (lastStep is not null
                && lastStep.AuthenticatedAuditRunId == auditRunId)
            {
                lastStep.WasFinalStep = true;
            }
        }


        auditRun.Status = "Completed";
        auditRun.CompletedAt = DateTime.UtcNow;
        auditRun.ErrorMessage = null;


        await dbContext.SaveChangesAsync(CancellationToken.None);
    }


    private async Task MarkRunAsInterruptedAsync(
    int auditRunId,
    string errorMessage)
    {
        await using AsyncServiceScope scope =
            _scopeFactory.CreateAsyncScope();


        ApplicationDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();


        AuthenticatedAuditRun? auditRun =
            await dbContext.AuthenticatedAuditRuns.FindAsync(
                new object[] { auditRunId },
                CancellationToken.None);


        if (auditRun is null)
        {
            return;
        }


        /*
         * Do not overwrite a run that another request successfully completed
         * while application shutdown was beginning.
         */
        if (auditRun.Status != "Running")
        {
            return;
        }


        auditRun.Status = "Interrupted";
        auditRun.CompletedAt = DateTime.UtcNow;
        auditRun.ErrorMessage = LimitLength(errorMessage, 4000);


        /*
         * Once shutdown owns the session, record its final state even when the
         * original HTTP request or application cancellation token is cancelled.
         */
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }


    private async Task MarkRunAsUnsuccessfulAsync(
        int auditRunId,
        string status,
        string? errorMessage)
    {
        try
        {
            await using AsyncServiceScope scope =
                _scopeFactory.CreateAsyncScope();


            ApplicationDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();


            AuthenticatedAuditRun? auditRun =
                await dbContext.AuthenticatedAuditRuns.FindAsync(
                    new object[] { auditRunId },
                    CancellationToken.None);


            if (auditRun is null)
            {
                return;
            }


            if (auditRun.Status != "Running") return;
            auditRun.Status = status;
            auditRun.CompletedAt = DateTime.UtcNow;
            auditRun.ErrorMessage = LimitLength(errorMessage, 4000);


            // Do not use the original cancellation token here. Even if the
            // request was cancelled, we still want to record what happened.
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception databaseException)
        {
            // A database logging failure should not hide the original
            // Playwright startup exception.
            _logger.LogError(
                databaseException,
                "Could not update unsuccessful audit run {AuditRunId}.",
                auditRunId);
            throw;
        }
    }


    private static string BuildElementFixGuidance(
    string ruleId)
    {
        string normalizedRuleId =
            ruleId.Trim().ToLowerInvariant();


        string guidance =
            normalizedRuleId switch
            {
                "label" =>
                    "Add a visible <label> for this form control. " +
                    "The label's for attribute must match the control's id. " +
                    "When a visible label is not appropriate, provide an accessible name using aria-label or aria-labelledby.",


                "button-name" =>
                    "Give this button an accessible name using visible text, " +
                    "aria-label, or aria-labelledby. The name should clearly describe the button's action.",


                "link-name" =>
                    "Add meaningful visible link text or provide an accessible name using aria-label or aria-labelledby. " +
                    "The accessible name should describe the link's destination or purpose.",


                "image-alt" =>
                    "Add an alt attribute that describes the image's purpose. " +
                    "Use alt=\"\" only when the image is decorative and should be ignored by assistive technology.",


                "input-image-alt" =>
                    "Add an alt attribute that describes the action performed by this image input.",


                "select-name" =>
                    "Associate this select control with a visible <label>, or provide an accessible name using " +
                    "aria-label or aria-labelledby.",


                "color-contrast" =>
                    "Change this element's foreground or background color until it meets the required WCAG contrast ratio. " +
                    "Normal text generally requires 4.5:1, while large text generally requires 3:1.",


                "aria-valid-attr-value" =>
                    "Correct or remove the invalid ARIA attribute value. " +
                    "When the value references another element ID, confirm that the referenced element exists on the page.",


                "aria-allowed-attr" =>
                    "Remove the ARIA attribute that is not allowed for this element or change the element's role " +
                    "to one that supports the attribute.",


                "aria-required-attr" =>
                    "Add the ARIA attribute required by this element's role and give it an appropriate value.",


                "aria-required-children" =>
                    "Add the required child roles inside this element, or change the parent role so that it matches " +
                    "the element's actual structure.",


                "aria-required-parent" =>
                    "Place this element inside a parent with the required ARIA role, or change the element's role " +
                    "to match its actual structure.",


                "frame-title" =>
                    "Add a concise and meaningful title attribute to this frame describing the content or purpose of the embedded page.",


                "duplicate-id-aria" =>
                    "Change this element's id so that every referenced ID on the page is unique. " +
                    "Update any labels or ARIA attributes that reference the old ID.",


                "nested-interactive" =>
                    "Remove the interactive control nested inside this element. " +
                    "Use one interactive element, or separate the controls so each can receive focus independently.",


                "heading-order" =>
                    "Change this heading level so the page follows a logical hierarchy without skipping heading levels.",


                "html-has-lang" =>
                    "Add a valid lang attribute to the page's <html> element, such as lang=\"en\".",


                "document-title" =>
                    "Add a meaningful <title> element inside the page's <head> that identifies the page or current workflow step.",


                "landmark-one-main" =>
                    "Place the page's primary content inside one <main> element or an element with role=\"main\". " +
                    "Only one main landmark should be present.",


                "region" =>
                    "Place this content inside an appropriate landmark such as <main>, <nav>, <header>, <footer>, or an explicitly labeled region.",


                _ =>
                    "Review this element using the axe-core failure explanation below. " +
                    "Update the element's HTML, accessible name, role, state, or relationship so the stated requirement is satisfied."
            };


        return guidance;
    }


    private static string? LimitLength(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
        {
            return value;
        }


        return value[..maximumLength];
    }


    /// <summary>
    /// Compares the requested and final navigation destinations while ignoring
    /// query strings and fragments, which protected applications commonly change.
    /// </summary>
}

