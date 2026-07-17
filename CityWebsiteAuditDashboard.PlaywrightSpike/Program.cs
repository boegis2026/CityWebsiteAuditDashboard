using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;

Console.WriteLine("Authenticated Rendered-Step Capture Spike");
Console.WriteLine("-----------------------------------------");
Console.WriteLine();

Console.Write("Paste the authorized HTTPS form URL: ");
string? startingUrl = Console.ReadLine()?.Trim();

if (!Uri.TryCreate(startingUrl, UriKind.Absolute, out Uri? authorizedUri) ||
    authorizedUri.Scheme != Uri.UriSchemeHttps)
{
    Console.WriteLine();
    Console.WriteLine("The URL must be a valid HTTPS address.");
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
    return;
}

string approvedHost = authorizedUri.Host;

Console.WriteLine();
Console.WriteLine($"Approved host: {approvedHost}");
Console.WriteLine();

try
{
    using var playwright = await Playwright.CreateAsync();

    await using var browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 100
        });

    // This remains a fresh, non-persistent browser context.
    // No authentication state is loaded from or saved to disk.
    await using var context = await browser.NewContextAsync(
        new BrowserNewContextOptions
        {
            AcceptDownloads = false
        });

    var page = await context.NewPageAsync();

    Console.WriteLine("Opening the authorized application...");

    await page.GotoAsync(
        authorizedUri.ToString(),
        new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });

    Console.WriteLine();
    Console.WriteLine("Complete the Angeleno login manually.");
    Console.WriteLine();
    Console.WriteLine("After login:");
    Console.WriteLine("1. Navigate manually to the approved form.");
    Console.WriteLine("2. Stop on the first form step.");
    Console.WriteLine("3. Do not enter real personal information.");
    Console.WriteLine("4. Do not click a final Submit button.");
    Console.WriteLine();
    Console.WriteLine("When the first form step is fully visible,");
    Console.WriteLine("return to this console and press Enter.");

    Console.ReadLine();
    Console.WriteLine();
    Console.WriteLine("Waiting briefly for the rendered form to stabilize...");

    await page.WaitForTimeoutAsync(2_000);

    await PrintBrowserStructureAsync(context);

    IPage formPage = await FindBestFormPageAsync(
        context,
        approvedHost);

    Console.WriteLine();
    Console.WriteLine("Selected page for rendered-step capture:");
    Console.WriteLine(
        SanitizeUrlForDisplay(formPage.Url));

    const int maximumSteps = 5;

    var completedSteps =
        new List<AuthenticatedStepScanResult>();

    var seenFingerprints =
        new HashSet<string>(StringComparer.Ordinal);

    RenderedStepSnapshot currentSnapshot =
        await CaptureRenderedStepAsync(
            formPage,
            approvedHost);

    for (int stepNumber = 1;
         stepNumber <= maximumSteps;
         stepNumber++)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"RENDERED STATE {stepNumber} OF {maximumSteps}");

        PrintSnapshot(currentSnapshot);

        if (!seenFingerprints.Add(
                currentSnapshot.DomFingerprint))
        {
            Console.WriteLine();
            Console.WriteLine(
                "STOPPED: This rendered state was already scanned.");

            Console.WriteLine(
                "Duplicate-state detection prevented a loop.");

            break;
        }

        AccessibilityStepResult accessibilityResult =
            await RunAxeProofOfConceptAsync(
                formPage,
                currentSnapshot.StepIndicator);
        await InstallFinalActionBlockerAsync(formPage);

        Console.WriteLine();
        Console.WriteLine(
            "Final-action safety blocker is active for this page.");

        completedSteps.Add(
            new AuthenticatedStepScanResult(
                SequenceNumber: stepNumber,
                Snapshot: currentSnapshot,
                Accessibility: accessibilityResult));

        string[] riskyActionLabels =
            await GetVisibleRiskyActionLabelsAsync(formPage);

        if (riskyActionLabels.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("-----------------------------------------");
            Console.WriteLine("SAFE STOP CONDITION DETECTED");
            Console.WriteLine("-----------------------------------------");
            Console.WriteLine();

            Console.WriteLine(
                "A potentially final or consequential action " +
                "is visible:");

            foreach (string label in riskyActionLabels)
            {
                Console.WriteLine($"- {label}");
            }

            Console.WriteLine();
            Console.WriteLine(
                "The audit will stop without asking you " +
                "to continue.");

            break;
        }

        if (stepNumber == maximumSteps)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"Maximum step count of {maximumSteps} reached.");

            break;
        }

        int pageCountBeforeTransition =
            context.Pages.Count;

        Console.WriteLine();
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine("MANUAL NAVIGATION");
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine();
        Console.WriteLine("In the visible browser:");
        Console.WriteLine(
            "1. Use only approved fake test information.");

        Console.WriteLine(
            "2. Complete only the current form step.");

        Console.WriteLine(
            "3. Click only the normal Next or Continue button.");

        Console.WriteLine(
            "4. Do not click Submit, Finish, Certify, Pay, " +
            "Apply, or Finalize.");

        Console.WriteLine(
            "5. Wait until the next state is fully visible.");

        Console.WriteLine();
        Console.WriteLine(
            "Return here and type C to capture the next state.");

        Console.WriteLine(
            "Type S to stop the audit safely.");

        Console.Write("> ");

        string command =
            (Console.ReadLine() ?? string.Empty)
            .Trim();

        if (!string.Equals(
                command,
                "C",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine(
                "Audit stopped by the user.");

            break;
        }

        formPage =
            await ResolveFormPageAfterManualNavigationAsync(
                context,
                formPage,
                pageCountBeforeTransition,
                approvedHost);

        Console.WriteLine();
        Console.WriteLine(
            "Waiting for a distinct rendered state...");

        RenderedStepSnapshot nextSnapshot =
    await CaptureAfterTransitionAsync(
        formPage,
        approvedHost,
        currentSnapshot.DomFingerprint);

        bool stateChanged =
            !string.Equals(
                nextSnapshot.DomFingerprint,
                currentSnapshot.DomFingerprint,
                StringComparison.Ordinal);

        if (!stateChanged)
        {
            Console.WriteLine();
            Console.WriteLine(
                "STOPPED: No distinct rendered state was detected.");

            Console.WriteLine(
                $"Current step indicator: {nextSnapshot.StepIndicator}");

            await PrintVisibleValidationMessagesAsync(formPage);

            Console.WriteLine();
            Console.WriteLine(
                "Check the visible browser. The form either remained " +
                "on the same step or displayed validation feedback.");

            break;
        }

        PrintStateComparison(
            currentSnapshot,
            nextSnapshot);

        // This is essential. The next loop iteration must use
        // the newly detected form state.
        currentSnapshot = nextSnapshot;

        Console.WriteLine();
        Console.WriteLine(
            "Distinct state detected. Continuing to the next scan.");
    }

        PrintAuthenticatedAuditSummary(completedSteps);

        Console.WriteLine();
        Console.WriteLine(
            "Press Enter to close the browser and destroy " +
            "the authenticated session.");

        Console.ReadLine();
}

catch (InvalidOperationException exception)
{
    Console.WriteLine();
    Console.WriteLine("Safety check stopped the capture:");
    Console.WriteLine(exception.Message);
    Console.WriteLine();
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
}
catch (PlaywrightException exception)
{
    Console.WriteLine();
    Console.WriteLine("Playwright reported an error:");
    Console.WriteLine(exception.Message);
    Console.WriteLine();
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
}
catch (Exception exception)
{
    Console.WriteLine();
    Console.WriteLine("Unexpected error:");
    Console.WriteLine(exception);
    Console.WriteLine();
    Console.WriteLine("Press Enter to close.");
    Console.ReadLine();
}

static async Task<IPage> FindBestFormPageAsync(
    IBrowserContext context,
    string approvedHost)
{
    IPage? bestPage = null;
    int bestScore = -1;

    foreach (IPage candidatePage in context.Pages)
    {
        if (candidatePage.IsClosed)
        {
            continue;
        }

        if (!Uri.TryCreate(
                candidatePage.Url,
                UriKind.Absolute,
                out Uri? candidateUri))
        {
            continue;
        }

        if (!string.Equals(
                candidateUri.Host,
                approvedHost,
                StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        try
        {
            await candidatePage.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions
                {
                    Timeout = 10_000
                });

            int visibleFields = await candidatePage
                .Locator(
                    "input:not([type='hidden']):visible, " +
                    "select:visible, " +
                    "textarea:visible, " +
                    "[contenteditable='true']:visible")
                .CountAsync();

            int visibleButtons = await candidatePage
                .Locator(
                    "button:visible, " +
                    "input[type='button']:visible, " +
                    "input[type='submit']:visible")
                .CountAsync();

            int forms = await candidatePage
                .Locator("form")
                .CountAsync();

            // Fields are the strongest indication that this is the
            // actual application form rather than a landing page.
            int score =
                (visibleFields * 100) +
                (forms * 10) +
                visibleButtons;

            Console.WriteLine();
            Console.WriteLine(
                $"Candidate page: " +
                $"{SanitizeUrlForDisplay(candidatePage.Url)}");

            Console.WriteLine(
                $"Selection score: {score} " +
                $"(Fields={visibleFields}, " +
                $"Forms={forms}, " +
                $"Buttons={visibleButtons})");

            if (score > bestScore)
            {
                bestScore = score;
                bestPage = candidatePage;
            }
        }
        catch (PlaywrightException)
        {
            // The page may have closed or changed while being inspected.
            // Continue checking the remaining pages.
        }
    }

    if (bestPage is null)
    {
        throw new InvalidOperationException(
            "No open page on the approved host could be selected.");
    }

    return bestPage;
}

static async Task InstallFinalActionBlockerAsync(IPage page)
{
    await page.EvaluateAsync(
        """
        () => {
            if (window.__cityAuditFinalActionBlockerInstalled) {
                return;
            }

            window.__cityAuditFinalActionBlockerInstalled = true;

            const normalize = value =>
                (value ?? "")
                    .replace(/\s+/g, " ")
                    .trim();

            const riskyPattern =
                /\b(submit|finish|certify|pay|payment|apply|finalize)\b/i;

            const allowedNavigationPattern =
                /\b(next|continue|previous|back)\b/i;

            const getControlLabel = element => {
                if (!element) {
                    return "";
                }

                if (element instanceof HTMLInputElement) {
                    return normalize(
                        element.value
                        || element.getAttribute("aria-label")
                        || element.getAttribute("title")
                    );
                }

                return normalize(
                    element.innerText
                    || element.textContent
                    || element.getAttribute("aria-label")
                    || element.getAttribute("title")
                );
            };

            const shouldBlock = element => {
                const label = getControlLabel(element);

                if (!label) {
                    return false;
                }

                if (allowedNavigationPattern.test(label)) {
                    return false;
                }

                return riskyPattern.test(label);
            };

            document.addEventListener(
                "click",
                event => {
                    const control = event.target?.closest(
                        "button, " +
                        "input[type='button'], " +
                        "input[type='submit'], " +
                        "[role='button']"
                    );

                    if (!control || !shouldBlock(control)) {
                        return;
                    }

                    event.preventDefault();
                    event.stopPropagation();
                    event.stopImmediatePropagation();

                    const label = getControlLabel(control);

                    window.alert(
                        `Blocked by City Audit safety control: "${label}".`
                    );
                },
                true
            );

            document.addEventListener(
                "submit",
                event => {
                    const submitter = event.submitter;

                    if (!submitter || !shouldBlock(submitter)) {
                        return;
                    }

                    event.preventDefault();
                    event.stopPropagation();
                    event.stopImmediatePropagation();

                    const label = getControlLabel(submitter);

                    window.alert(
                        `Submission blocked by City Audit safety control: "${label}".`
                    );
                },
                true
            );
        }
        """);
}

static async Task PrintVisibleValidationMessagesAsync(
    IPage page)
{
    string[] messages = await page.EvaluateAsync<string[]>(
        """
        () => {
            const normalize = value =>
                (value ?? "")
                    .replace(/\s+/g, " ")
                    .trim();

            const isVisible = element => {
                const style = window.getComputedStyle(element);
                const rectangle = element.getBoundingClientRect();

                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rectangle.width > 0
                    && rectangle.height > 0;
            };

            const selectors = [
                "[role='alert']",
                ".validation-summary-errors",
                ".field-validation-error",
                ".invalid-feedback",
                ".text-danger",
                "[aria-invalid='true'] + span"
            ];

            return [...new Set(
                Array.from(
                    document.querySelectorAll(
                        selectors.join(",")
                    )
                )
                .filter(isVisible)
                .map(element =>
                    normalize(element.textContent)
                )
                .filter(message =>
                    message.length > 0
                )
            )].slice(0, 10);
        }
        """);

    Console.WriteLine();
    Console.WriteLine("Visible validation messages:");

    if (messages.Length == 0)
    {
        Console.WriteLine(
            "(No recognized validation messages detected.)");

        return;
    }

    foreach (string message in messages)
    {
        Console.WriteLine($"- {message}");
    }
}

static async Task<IPage>
    ResolveFormPageAfterManualNavigationAsync(
        IBrowserContext context,
        IPage currentPage,
        int previousPageCount,
        string approvedHost)
{
    IReadOnlyList<IPage> pages =
        context.Pages;

    // Prefer a newly opened approved tab or popup.
    if (pages.Count > previousPageCount)
    {
        for (int index = pages.Count - 1;
             index >= previousPageCount;
             index--)
        {
            IPage candidate = pages[index];

            if (IsPageOnApprovedHost(
                    candidate,
                    approvedHost))
            {
                return candidate;
            }
        }
    }

    // Most multi-step forms update the existing tab.
    if (!currentPage.IsClosed &&
        IsPageOnApprovedHost(
            currentPage,
            approvedHost))
    {
        return currentPage;
    }

    // Fall back to examining the remaining open pages.
    return await FindBestFormPageAsync(
        context,
        approvedHost);
}

static bool IsPageOnApprovedHost(
    IPage page,
    string approvedHost)
{
    if (!Uri.TryCreate(
            page.Url,
            UriKind.Absolute,
            out Uri? uri))
    {
        return false;
    }

    return string.Equals(
        uri.Host,
        approvedHost,
        StringComparison.OrdinalIgnoreCase);
}

static async Task<string[]>
    GetVisibleRiskyActionLabelsAsync(IPage page)
{
    // This reads labels only from visible buttons and button-like
    // inputs. It does not read text-field or textarea values.

    return await page.EvaluateAsync<string[]>(
        """
        () => {
            const isVisible = element => {
                const style = window.getComputedStyle(element);
                const rectangle = element.getBoundingClientRect();

                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rectangle.width > 0
                    && rectangle.height > 0;
            };

            const normalize = value =>
                (value ?? "")
                    .replace(/\s+/g, " ")
                    .trim();

            const riskyPattern =
                /\b(submit|finish|certify|pay|payment|apply|finalize)\b/i;

            const labels = Array
                .from(document.querySelectorAll(
                    "button, " +
                    "input[type='button'], " +
                    "input[type='submit']"))
                .filter(isVisible)
                .map(element => {
                    if (element instanceof HTMLInputElement) {
                        return normalize(
                            element.value
                            || element.getAttribute("aria-label"));
                    }

                    return normalize(
                        element.innerText
                        || element.textContent
                        || element.getAttribute("aria-label"));
                })
                .filter(label =>
                    label.length > 0 &&
                    riskyPattern.test(label));

            return [...new Set(labels)].slice(0, 10);
        }
        """);
}

static async Task<RenderedStepSnapshot> CaptureAfterTransitionAsync(
    IPage page,
    string approvedHost,
    string previousFingerprint)
{
    DateTimeOffset deadline =
        DateTimeOffset.UtcNow.AddSeconds(15);

    RenderedStepSnapshot latestSnapshot =
        await CaptureRenderedStepAsync(
            page,
            approvedHost);

    while (
        latestSnapshot.DomFingerprint == previousFingerprint &&
        DateTimeOffset.UtcNow < deadline)
    {
        await page.WaitForTimeoutAsync(500);

        latestSnapshot =
            await CaptureRenderedStepAsync(
                page,
                approvedHost);
    }

    return latestSnapshot;
}

static async Task<RenderedStepSnapshot> CaptureRenderedStepAsync(
    IPage page,
    string approvedHost)
{
    if (!Uri.TryCreate(page.Url, UriKind.Absolute, out Uri? currentUri))
    {
        throw new InvalidOperationException(
            "The current browser URL could not be validated.");
    }

    if (!string.Equals(
            currentUri.Host,
            approvedHost,
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"The browser is on '{currentUri.Host}', but only " +
            $"'{approvedHost}' is approved for this capture.");
    }

    // A visible password field strongly suggests that the browser
    // may still be showing an authentication page.
    int visiblePasswordFields = await page
        .Locator("input[type='password']:visible")
        .CountAsync();

    if (visiblePasswordFields > 0)
    {
        throw new InvalidOperationException(
            "A visible password field was detected. " +
            "The protected form may not be loaded yet.");
    }

    string title = NormalizeText(await page.TitleAsync());

    string mainHeading = await GetMainHeadingAsync(page);

    string stepIndicator = await GetStepIndicatorAsync(page);

    int formCount = await page
        .Locator("form:visible")
        .CountAsync();

    int fieldCount = await page
    .Locator(
        "input:not([type='hidden']):visible, " +
        "select:visible, " +
        "textarea:visible, " +
        "[contenteditable='true']:visible")
    .CountAsync();

    int buttonCount = await page
        .Locator(
            "button:visible, " +
            "input[type='button']:visible, " +
            "input[type='submit']:visible")
        .CountAsync();

    int iframeCount = await page
        .Locator("iframe:visible")
        .CountAsync();

    int dialogCount = await page
        .Locator(
            "[role='dialog']:visible, dialog[open]:visible")
        .CountAsync();

    string structuralSignature =
        await BuildStructuralSignatureAsync(page);

    string fingerprint = CreateSha256(structuralSignature);

    return new RenderedStepSnapshot(
        SafeUrl: SanitizeUrlForDisplay(page.Url),
        PageTitle: title,
        MainHeading: mainHeading,
        StepIndicator: stepIndicator,
        FormCount: formCount,
        FieldCount: fieldCount,
        ButtonCount: buttonCount,
        IframeCount: iframeCount,
        DialogCount: dialogCount,
        DomFingerprint: fingerprint,
        CapturedAtUtc: DateTimeOffset.UtcNow);
}

static async Task<string> GetMainHeadingAsync(IPage page)
{
    string[] selectors =
    {
        "h1:visible",
        "[role='heading'][aria-level='1']:visible",
        "main h2:visible",
        ".page-title:visible",
        ".page-header:visible",
        ".card-title:visible",
        ".title:visible"
    };

    foreach (string selector in selectors)
    {
        ILocator locator = page.Locator(selector).First;

        if (await locator.CountAsync() == 0)
        {
            continue;
        }

        string text = NormalizeText(
            await locator.InnerTextAsync());

        if (text != "(Not available)")
        {
            return text;
        }
    }

    return "(No recognizable visible page heading detected.)";
}

static async Task<string> GetStepIndicatorAsync(IPage page)
{
    ILocator currentStepLocator = page
        .Locator(
            "[aria-current='step']:visible, " +
            ".step.active:visible, " +
            ".current-step:visible, " +
            "[data-current-step]:visible")
        .First;

    if (await currentStepLocator.CountAsync() > 0)
    {
        string text =
            NormalizeText(await currentStepLocator.InnerTextAsync());

        if (text != "(Not available)")
        {
            return text;
        }

        string? ariaLabel =
            await currentStepLocator.GetAttributeAsync("aria-label");

        if (!string.IsNullOrWhiteSpace(ariaLabel))
        {
            return NormalizeText(ariaLabel);
        }

        string? dataStep =
            await currentStepLocator.GetAttributeAsync(
                "data-current-step");

        if (!string.IsNullOrWhiteSpace(dataStep))
        {
            return $"Step {NormalizeText(dataStep)}";
        }
    }

    ILocator progressBar = page
        .Locator("[role='progressbar']:visible")
        .First;

    if (await progressBar.CountAsync() > 0)
    {
        string? valueText =
            await progressBar.GetAttributeAsync("aria-valuetext");

        if (!string.IsNullOrWhiteSpace(valueText))
        {
            return NormalizeText(valueText);
        }

        string? currentValue =
            await progressBar.GetAttributeAsync("aria-valuenow");

        string? maximumValue =
            await progressBar.GetAttributeAsync("aria-valuemax");

        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            return string.IsNullOrWhiteSpace(maximumValue)
                ? $"Progress value {currentValue}"
                : $"Progress {currentValue} of {maximumValue}";
        }
    }

    return "(Not detected automatically.)";
}

static async Task<string> BuildStructuralSignatureAsync(IPage page)
{
    // This script deliberately excludes all entered form values,
    // cookies, browser storage, query strings, and hidden-field values.

    return await page.EvaluateAsync<string>(
        """
        () => {
            const normalize = value =>
                (value ?? "").replace(/\s+/g, " ").trim();

            const isVisible = element => {
                const style = window.getComputedStyle(element);
                const rectangle = element.getBoundingClientRect();

                return style.display !== "none"
                    && style.visibility !== "hidden"
                    && rectangle.width > 0
                    && rectangle.height > 0;
            };

            const headings = Array
                .from(document.querySelectorAll(
                    "h1, h2, h3, [role='heading']"))
                .filter(isVisible)
                .map((element, index) => ({
                    order: index,
                    tag: element.tagName.toLowerCase(),
                    level:
                        element.getAttribute("aria-level") ?? "",
                    text:
                        normalize(element.textContent).slice(0, 160)
                }));

            const stateLabels = Array
                .from(document.querySelectorAll(
                    "[aria-current='step'], " +
                    ".step.active, " +
                    ".current-step, " +
                    "[data-current-step], " +
                    "[role='progressbar']"))
                .filter(isVisible)
                .map((element, index) => ({
                    order: index,
                    text:
                        normalize(element.textContent).slice(0, 160),
                    ariaLabel:
                        element.getAttribute("aria-label") ?? "",
                    ariaValueText:
                        element.getAttribute("aria-valuetext") ?? "",
                    ariaValueNow:
                        element.getAttribute("aria-valuenow") ?? "",
                    currentStep:
                        element.getAttribute("data-current-step") ?? ""
                }));

            const controls = Array
                .from(document.querySelectorAll(
                    "input, select, textarea, button"))
                .filter(isVisible)
                .map((element, index) => ({
                    order: index,
                    tag: element.tagName.toLowerCase(),
                    type:
                        element.getAttribute("type") ?? "",
                    role:
                        element.getAttribute("role") ?? "",
                    name:
                        element.getAttribute("name") ?? "",
                    id:
                        element.id ?? "",
                    required:
                        element.hasAttribute("required"),
                    disabled:
                        element.hasAttribute("disabled")
                }));

            return JSON.stringify({
                path: window.location.pathname,
                headings: headings,
                stateLabels: stateLabels,
                controls: controls,
                formCount:
                    document.querySelectorAll("form").length,
                iframeCount:
                    document.querySelectorAll("iframe").length,
                dialogCount:
                    document.querySelectorAll(
                        "[role='dialog'], dialog[open]"
                    ).length
            });
        }
        """);
}

static string CreateSha256(string value)
{
    byte[] valueBytes = Encoding.UTF8.GetBytes(value);

    byte[] hashBytes = SHA256.HashData(valueBytes);

    return Convert.ToHexString(hashBytes);
}

static string SanitizeUrlForDisplay(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
    {
        return "(Invalid or unavailable URL)";
    }

    // Query strings and fragments are deliberately excluded.
    return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
}

static string NormalizeText(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "(Not available)";
    }

    char[] whitespaceSeparators =
    {
        ' ',
        '\t',
        '\r',
        '\n'
    };

    return string.Join(
        " ",
        value.Split(
            whitespaceSeparators,
            StringSplitOptions.RemoveEmptyEntries));
}

static void PrintSnapshot(RenderedStepSnapshot snapshot)
{
    Console.WriteLine();
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine("PROTECTED RENDERED-STEP SNAPSHOT");
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine();
    Console.WriteLine($"Captured UTC:   {snapshot.CapturedAtUtc:O}");
    Console.WriteLine($"Safe URL:       {snapshot.SafeUrl}");
    Console.WriteLine($"Page title:     {snapshot.PageTitle}");
    Console.WriteLine($"Main heading:   {snapshot.MainHeading}");
    Console.WriteLine($"Step indicator: {snapshot.StepIndicator}");
    Console.WriteLine();
    Console.WriteLine($"Visible forms:  {snapshot.FormCount}");
    Console.WriteLine($"Visible fields: {snapshot.FieldCount}");
    Console.WriteLine($"Visible buttons:{snapshot.ButtonCount}");
    Console.WriteLine($"Visible iframes:{snapshot.IframeCount}");
    Console.WriteLine($"Visible dialogs:{snapshot.DialogCount}");
    Console.WriteLine();
    Console.WriteLine("DOM fingerprint:");
    Console.WriteLine(snapshot.DomFingerprint);
}

static void PrintStateComparison(
    RenderedStepSnapshot first,
    RenderedStepSnapshot second)
{
    bool fingerprintChanged =
        !string.Equals(
            first.DomFingerprint,
            second.DomFingerprint,
            StringComparison.Ordinal);

    bool urlChanged =
        !string.Equals(
            first.SafeUrl,
            second.SafeUrl,
            StringComparison.OrdinalIgnoreCase);

    bool stepIndicatorChanged =
        !string.Equals(
            first.StepIndicator,
            second.StepIndicator,
            StringComparison.Ordinal);

    bool controlCountsChanged =
        first.FieldCount != second.FieldCount ||
        first.ButtonCount != second.ButtonCount ||
        first.FormCount != second.FormCount;

    Console.WriteLine();
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine("STATE COMPARISON");
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine();

    Console.WriteLine(
        $"URL changed:            {YesNo(urlChanged)}");

    Console.WriteLine(
        $"Step indicator changed: {YesNo(stepIndicatorChanged)}");

    Console.WriteLine(
        $"Control counts changed: {YesNo(controlCountsChanged)}");

    Console.WriteLine(
        $"DOM fingerprint changed:{YesNo(fingerprintChanged)}");

    Console.WriteLine();

    if (fingerprintChanged)
    {
        Console.WriteLine(
            "SUCCESS: A distinct rendered form state was detected.");
    }
    else
    {
        Console.WriteLine(
            "NO DISTINCT STATE DETECTED.");

        Console.WriteLine(
            "The form may have stayed on the same step, " +
            "shown validation errors, or changed content that " +
            "is not yet represented in the fingerprint.");
    }
}

static void PrintAccessibilityComparison(
    AccessibilityStepResult first,
    AccessibilityStepResult second)
{
    Console.WriteLine();
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine("ACCESSIBILITY RESULTS BY FORM STEP");
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine();

    PrintAccessibilityResultRow(first);
    PrintAccessibilityResultRow(second);

    bool resultsDiffer =
    first.ViolationRuleCount != second.ViolationRuleCount ||
    first.AffectedElementCount != second.AffectedElementCount ||
    first.IncompleteRuleCount != second.IncompleteRuleCount ||
    first.PassedRuleCount != second.PassedRuleCount ||
    !first.ViolationRuleIds.SequenceEqual(
        second.ViolationRuleIds);

    Console.WriteLine();
    Console.WriteLine(
        $"Results differ by rendered state: {YesNo(resultsDiffer)}");

    Console.WriteLine();
    Console.WriteLine(
        "SUCCESS: Each authenticated form state was scanned " +
        "and represented by a separate accessibility result.");
}

static void PrintAuthenticatedAuditSummary(
    IReadOnlyList<AuthenticatedStepScanResult> steps)
{
    Console.WriteLine();
    Console.WriteLine("=========================================");
    Console.WriteLine("IN-MEMORY AUTHENTICATED AUDIT SUMMARY");
    Console.WriteLine("=========================================");
    Console.WriteLine();

    if (steps.Count == 0)
    {
        Console.WriteLine(
            "No rendered states were successfully scanned.");

        return;
    }

    foreach (AuthenticatedStepScanResult step in steps)
    {
        Console.WriteLine(
            $"Step {step.SequenceNumber}: " +
            $"{step.Snapshot.StepIndicator}");

        Console.WriteLine(
            $"  Fields:            " +
            $"{step.Snapshot.FieldCount}");

        Console.WriteLine(
            $"  Buttons:           " +
            $"{step.Snapshot.ButtonCount}");

        Console.WriteLine(
            $"  Violation rules:   " +
            $"{step.Accessibility.ViolationRuleCount}");

        Console.WriteLine(
            $"  Affected elements: " +
            $"{step.Accessibility.AffectedElementCount}");

        Console.WriteLine(
            $"  Needs review:      " +
            $"{step.Accessibility.IncompleteRuleCount}");

        Console.WriteLine(
            $"  Passed rules:      " +
            $"{step.Accessibility.PassedRuleCount}");

        Console.WriteLine(
            $"  Scanned UTC:       " +
            $"{step.Accessibility.ScannedAtUtc:O}");

        Console.WriteLine();
    }

    Console.WriteLine(
        $"Total distinct states scanned: {steps.Count}");

    Console.WriteLine();
    Console.WriteLine(
        "These results exist only in application memory.");

    Console.WriteLine(
        "Nothing from this run was saved to SQL Server.");
}

static void PrintAccessibilityResultRow(
    AccessibilityStepResult result)
{
    Console.WriteLine($"Step: {result.StepName}");
    Console.WriteLine(
        $"  Violation rules:  {result.ViolationRuleCount}");

    Console.WriteLine(
        $"  Affected elements:{result.AffectedElementCount}");

    Console.WriteLine(
        $"  Needs review:     {result.IncompleteRuleCount}");

    Console.WriteLine(
        $"  Passed rules:     {result.PassedRuleCount}");

    string ruleList =
        result.ViolationRuleIds.Length == 0
            ? "(None)"
            : string.Join(", ", result.ViolationRuleIds);

    Console.WriteLine($"  Rule IDs:         {ruleList}");
    Console.WriteLine();
}

static string YesNo(bool value)
{
    return value ? "YES" : "NO";
}

static async Task PrintBrowserStructureAsync(
    IBrowserContext context)
{
    Console.WriteLine();
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine("BROWSER PAGE AND FRAME DIAGNOSTIC");
    Console.WriteLine("-----------------------------------------");

    IReadOnlyList<IPage> pages = context.Pages;

    Console.WriteLine();
    Console.WriteLine($"Open browser pages/tabs: {pages.Count}");

    for (int pageIndex = 0; pageIndex < pages.Count; pageIndex++)
    {
        IPage currentPage = pages[pageIndex];

        string title;

        try
        {
            title = NormalizeText(await currentPage.TitleAsync());
        }
        catch
        {
            title = "(Title unavailable)";
        }

        Console.WriteLine();
        Console.WriteLine($"PAGE/TAB {pageIndex + 1}");
        Console.WriteLine($"Title: {title}");
        Console.WriteLine(
            $"URL:   {SanitizeUrlForDisplay(currentPage.Url)}");

        IReadOnlyList<IFrame> frames = currentPage.Frames;

        Console.WriteLine($"Frames on this page: {frames.Count}");

        for (int frameIndex = 0;
             frameIndex < frames.Count;
             frameIndex++)
        {
            IFrame frame = frames[frameIndex];

            int formCount = 0;
            int fieldCount = 0;
            int buttonCount = 0;
            int headingCount = 0;

            try
            {
                formCount = await frame
                    .Locator("form")
                    .CountAsync();

                fieldCount = await frame
                    .Locator(
                        "input:visible, " +
                        "select:visible, " +
                        "textarea:visible")
                    .CountAsync();

                buttonCount = await frame
                    .Locator(
                        "button:visible, " +
                        "input[type='button']:visible, " +
                        "input[type='submit']:visible")
                    .CountAsync();

                headingCount = await frame
                    .Locator(
                        "h1:visible, h2:visible, h3:visible, " +
                        "[role='heading']:visible")
                    .CountAsync();
            }
            catch (PlaywrightException)
            {
                // Continue printing the rest of the browser structure
                // if one frame disappears during dynamic rendering.
            }

            string frameType =
                frame == currentPage.MainFrame
                    ? "Main frame"
                    : "Child iframe";

            Console.WriteLine();
            Console.WriteLine(
                $"  FRAME {frameIndex + 1} — {frameType}");

            Console.WriteLine(
                $"  URL:      {SanitizeUrlForDisplay(frame.Url)}");

            Console.WriteLine(
                $"  Forms:    {formCount}");

            Console.WriteLine(
                $"  Fields:   {fieldCount}");

            Console.WriteLine(
                $"  Buttons:  {buttonCount}");

            Console.WriteLine(
                $"  Headings: {headingCount}");
        }
    }
}

static async Task<AccessibilityStepResult>
    RunAxeProofOfConceptAsync(
        IPage page,
        string stateName)
{
    Console.WriteLine();
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine("AXE-CORE TECHNICAL PROOF OF CONCEPT");
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine();
    Console.WriteLine($"Rendered state: {stateName}");
    Console.WriteLine("Running local accessibility analysis...");

    AxeResult results = await page.RunAxe();

    int violationRuleCount =
        results.Violations.Count();

    int violationNodeCount =
        results.Violations.Sum(
            violation => violation.Nodes.Count());

    int incompleteRuleCount =
        results.Incomplete.Count();

    int passedRuleCount =
        results.Passes.Count();

    string[] violationRuleIds =
        results.Violations
            .Select(violation => violation.Id)
            .OrderBy(ruleId => ruleId)
            .ToArray();

    Console.WriteLine();
    Console.WriteLine($"Scan timestamp:       {results.Timestamp:O}");
    Console.WriteLine($"Violation rule types: {violationRuleCount}");
    Console.WriteLine($"Affected elements:    {violationNodeCount}");
    Console.WriteLine($"Needs review rules:   {incompleteRuleCount}");
    Console.WriteLine($"Passed rules:         {passedRuleCount}");

    Console.WriteLine();
    Console.WriteLine("Violation summaries:");

    if (violationRuleCount == 0)
    {
        Console.WriteLine(
            "No automatically detectable axe violations were returned.");
    }
    else
    {
        foreach (var violation in results.Violations)
        {
            string impact =
                string.IsNullOrWhiteSpace(violation.Impact)
                    ? "(Impact not provided)"
                    : violation.Impact;

            Console.WriteLine(
                $"- {violation.Id} | " +
                $"Impact: {impact} | " +
                $"Affected elements: {violation.Nodes.Count()}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        "Important: These are axe-core results, not WAVE results.");

    Console.WriteLine(
        "No element HTML, form values, cookies, or tokens were printed.");

    return new AccessibilityStepResult(
        StepName: stateName,
        ScannedAtUtc: results.Timestamp ?? DateTimeOffset.UtcNow,
        ViolationRuleCount: violationRuleCount,
        AffectedElementCount: violationNodeCount,
        IncompleteRuleCount: incompleteRuleCount,
        PassedRuleCount: passedRuleCount,
        ViolationRuleIds: violationRuleIds);
}

internal sealed record RenderedStepSnapshot(
    string SafeUrl,
    string PageTitle,
    string MainHeading,
    string StepIndicator,
    int FormCount,
    int FieldCount,
    int ButtonCount,
    int IframeCount,
    int DialogCount,
    string DomFingerprint,
    DateTimeOffset CapturedAtUtc);

internal sealed record AccessibilityStepResult(
    string StepName,
    DateTimeOffset ScannedAtUtc,
    int ViolationRuleCount,
    int AffectedElementCount,
    int IncompleteRuleCount,
    int PassedRuleCount,
    string[] ViolationRuleIds);

internal sealed record AuthenticatedStepScanResult(
    int SequenceNumber,
    RenderedStepSnapshot Snapshot,
    AccessibilityStepResult Accessibility);
