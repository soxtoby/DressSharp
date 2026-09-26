// Playwright run-code, against an already-open application with a disposable EditorConfig.
async page => {
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const failures = [];
    const onError = error => failures.push(error.message);
    page.on("pageerror", onError);
    const panel = page.getByRole("dialog");
    const output = page.getByLabel("Example output", {exact: true});
    const search = page.getByRole("searchbox", {name: "Find a preference"});
    const ready = () => page.waitForFunction(() => {
        const status = document.querySelector(".example-status")?.textContent;
        return status && status !== "Formatting example…";
    });
    const sourceText = () => page.getByRole("textbox", {name: "Preview.cs", exact: true})
        .evaluate(element => [...element.querySelectorAll("[data-line]")].map(line => line.textContent).join("\n"));
    const open = async key => {
        await search.fill(key);
        const button = page.locator(`[id="example-${key}"]`);
        await button.focus();
        await page.keyboard.press("Enter");
        await ready();
    };
    try {
        await page.setViewportSize({width: 1400, height: 900});
        await page.reload();
        await page.getByRole("status").filter({hasText: "Up to date"}).waitFor();
        const source = await sourceText();
        await search.fill("csharp_space_after_comma");
        await page.mouse.move(0, 0);
        await page.waitForFunction(() => getComputedStyle(document.querySelector(".rule-example")).opacity === "0");
        assert(await page.locator(".rule-example svg").count() === 1, "Example entry must be an icon, not a repeated text link");
        await page.locator(".rule-example").focus();
        await page.waitForFunction(() => getComputedStyle(document.querySelector(".rule-example")).opacity === "1");
        await page.locator(".rule-row select").selectOption("explicit:true");
        await page.getByRole("status").filter({hasText: "Up to date"}).waitFor();
        const save = await page.locator(".save").textContent();
        const configuration = await (await page.request.get(page.url() + "api/configuration")).text();
        const bootstrap = await (await page.request.get(page.url() + "api/bootstrap")).json();
        assert(await page.locator(".rule-example").count() === 1, "Search should expose its rule example");
        for (const rule of bootstrap.catalog.rules) {
            const request = page.waitForRequest(request => request.url().endsWith("/api/preview")
                && request.postDataJSON().preferences.some(preference => preference.key === rule.key));
            await open(rule.key);
            const sent = (await request).postDataJSON();
            assert(sent.source === rule.example, `${rule.key}: use catalog source exactly`);
            assert(sent.preferences.at(-1).key === rule.key && sent.preferences.at(-1).local.value === rule.defaultValue,
                `${rule.key}: isolate the selected rule and outcome`);
            assert(await panel.getByRole("heading", {level: 2}).textContent() === rule.expandedCaption, "Identify rule");
            await panel.getByRole("checkbox", {name: "Whitespace"}).uncheck();
            assert((await page.getByLabel("Input code", {exact: true}).locator("[data-line]").allTextContents()).map(line => line.replace(/\r?\n$/, "")).join("\n") === rule.example.replace(/\r\n/g, "\n").replace(/\n$/, ""), "Show input code");
            assert(sent.preferences.length === Object.keys(rule.examplePreferences).length + 1, "Include explicit companion settings only");
            assert(!(await panel.locator(".example-status").textContent()).startsWith("Example failed"), `${rule.key}: default outcome must format`);
            assert(await output.evaluate(element => !element.isContentEditable && !element.querySelector("[contenteditable=true]")), "Output must be read-only");
            await page.keyboard.press("Escape");
            assert(await panel.isHidden(), "Escape closes example");
            assert(await page.locator(`[id="example-${rule.key}"]`).evaluate(element => document.activeElement === element), "Escape restores trigger focus");
        }
        await open("csharp_space_after_comma");
        await page.getByLabel("Value", {exact: true}).selectOption("false");
        await ready();
        const compact = await output.textContent();
        await page.getByLabel("Value", {exact: true}).selectOption("true");
        await ready();
        assert(await output.textContent() !== compact, "Changing outcome must change the example");
        assert(await panel.locator('[data-line][data-line-type="change-deletion"]').count() > 0, "Highlight removed input lines");
        assert(await panel.locator('[data-line][data-line-type="change-addition"]').count() > 0, "Highlight added output lines");
        assert(await panel.locator("[data-diff-span]").count() > 0, "Highlight intra-line changes");
        const beforeWhitespace = await output.textContent();
        await panel.getByRole("checkbox", {name: "Whitespace"}).check();
        assert(await output.textContent() === beforeWhitespace, "Whitespace decorations preserve copyable code");
        const markers = await output.locator("[data-line]").nth(2).evaluate(line => getComputedStyle(line, "::after").content);
        assert(markers.includes("·"), "Diff whitespace markers are visible");
        await Promise.all([
            page.waitForResponse(response => response.url().endsWith("/api/configuration")),
            page.evaluate(() => window.dispatchEvent(new Event("focus"))),
        ]);
        assert(await panel.isVisible(), "Polling retains open example");
        await panel.getByRole("button", {name: "Close", exact: true}).click();
        assert(await page.locator('[id="example-csharp_space_after_comma"]').evaluate(element => document.activeElement === element), "Close restores focus after settings rerender");
        await open("indent_size");
        await page.getByLabel("Value", {exact: true}).fill("invalid");
        assert(await page.getByLabel("Value", {exact: true}).getAttribute("aria-invalid") === "true", "Invalid outcome is identified");
        await page.getByLabel("Value", {exact: true}).fill("2");
        await ready();
        assert(!(await panel.locator(".example-status").textContent()).includes("failed"), "Valid outcome recovers");
        const smallIndent = await output.textContent();
        await page.getByLabel("Value", {exact: true}).fill("8");
        await ready();
        assert(await output.textContent() !== smallIndent, "Indent size must visibly change code using its companion settings");
        await page.keyboard.press("Escape");
        await page.route("**/api/preview", route => route.fulfill({status: 500, contentType: "application/json", body: '{"message":"Example test failure"}'}));
        await open("end_of_line");
        assert((await panel.locator(".example-status").textContent()).includes("Example test failure"), "Show recoverable request error");
        await page.unroute("**/api/preview");
        await page.getByLabel("Value", {exact: true}).selectOption("crlf");
        await ready();
        assert((await panel.locator(".example-status").textContent()).includes("CRLF"), "Representation-only outcome is visible");
        await page.setViewportSize({width: 375, height: 700});
        const geometry = await panel.evaluate(element => {
            const bounds = element.getBoundingClientRect();
            const columns = element.querySelector("diffs-container").shadowRoot.querySelectorAll("[data-deletions], [data-additions]");
            return {fits: bounds.left >= 0 && bounds.right <= innerWidth && bounds.bottom <= innerHeight,
                stacked: columns[1].getBoundingClientRect().top >= columns[0].getBoundingClientRect().bottom,
                overflow: element.scrollWidth > element.clientWidth};
        });
        assert(geometry.fits && geometry.stacked && !geometry.overflow, "Mobile example fits and stacks without panel overflow");
        await page.screenshot({path: ".tmp/sox156/examples-mobile.png"});
        await page.setViewportSize({width: 1400, height: 900});
        await page.screenshot({path: ".tmp/sox156/examples-desktop.png"});
        await page.keyboard.press("Escape");
        assert(await sourceText() === source, "Examples never alter preview source");
        assert(await page.locator(".save").textContent() === save, "Examples never alter pending preferences");
        assert(await (await page.request.get(page.url() + "api/configuration")).text() === configuration, "Examples never write configuration");
        assert(failures.length === 0, `Browser errors: ${failures.join("; ")}`);
        return `${bootstrap.catalog.rules.length} catalog examples passed: isolated outcomes, keyboard opening/closing, polling, recovery, representation metadata, mobile, source/preferences preserved.`;
    } finally {
        await page.unroute("**/api/preview");
        page.off("pageerror", onError);
    }
}
