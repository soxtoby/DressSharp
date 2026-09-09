// Run with Playwright's run-code against an already-open interactive application
// using a disposable EditorConfig. This exercises Save and changes that configuration.
async page => {
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const failures = [];
    const onError = error => failures.push(error.message);
    const external = [];
    const origin = page.url().split("/").slice(0, 3).join("/");
    const onRequest = request => { if (!request.url().startsWith(origin + "/")) external.push(request.url()); };
    page.on("pageerror", onError);
    page.on("request", onRequest);
    const ready = () => page.getByRole("status").filter({hasText: "Up to date"}).waitFor();
    const editor = () => page.getByRole("textbox", {name: "Preview.cs", exact: true});
    const sourceText = () => editor().evaluate(element => [...element.querySelectorAll("[data-line]")].map(line => line.textContent).join("\n"));
    const paste = async text => {
        await editor().click();
        await page.keyboard.press("Control+a");
        const request = page.waitForRequest(request => request.url().endsWith("/api/preview"));
        await editor().evaluate((element, text) => {
            const data = new DataTransfer();
            data.setData("text/plain", text);
            element.dispatchEvent(new ClipboardEvent("paste", {bubbles: true, clipboardData: data}));
        }, text);
        return (await request).postDataJSON();
    };
    try {
        await page.setViewportSize({width: 1400, height: 900});
        await page.reload();
        await ready();
        const sent = await paste("class C { void M(int a,int b) {} }\r\n");
        assert(sent.source.endsWith("\n") && !sent.source.includes("\r"), "Paste must normalize to LF");
        await ready();
        await page.getByRole("searchbox", {name: "Find a preference"}).fill("csharp_space_after_comma");
        await page.locator(".rule-row select").selectOption("explicit:true");
        await ready();
        assert(await page.locator("[data-deletions] [data-content]").textContent().then(text => text.includes("int a, int b")), "Real pending preference must format output");
        const source = await sourceText();
        await page.getByRole("checkbox", {name: "Whitespace"}).check();
        const markers = await page.locator("[data-additions] [data-line]").first().evaluate(line => getComputedStyle(line, "::after").content);
        assert(markers.includes("·") && markers.includes("↵"), "Whitespace markers must be visible");
        assert(await sourceText() === source, "Whitespace must not mutate source");
        const longSource = ["class LongPreview {", "    void First(int a,int b) {}",
            ...Array.from({length: 80}, (_, index) => `    // context ${index + 1}`),
            "    void Last(int a,int b) {}", "}", ""].join("\n");
        await paste(longSource);
        await ready();
        const rulerMarkers = page.getByRole("navigation", {name: "Differences in preview"}).locator("button");
        assert(await rulerMarkers.count() >= 2, "Overview ruler must mark separated differences");
        await rulerMarkers.last().click();
        await page.waitForFunction(() => document.querySelector(".diff-scroll").scrollTop > 0);
        await paste(source);
        await ready();
        await editor().click();
        await page.keyboard.press("Control+Home");
        await page.keyboard.type("// undo test");
        await page.keyboard.press("Enter");
        await ready();
        // Poll replaces controls, but must leave the editor and its focus intact.
        await page.evaluate(() => window.dispatchEvent(new Event("focus")));
        await page.waitForResponse(response => response.url().endsWith("/api/configuration"));
        assert(await editor().evaluate(element => element.getRootNode().activeElement === element), "Polling must preserve editor focus");
        await page.keyboard.press("Control+z");
        await page.keyboard.press("Control+z");
        await ready();
        assert(await sourceText() === source, "Undo must survive output refresh and polling");
        await page.locator(".rule-row select").selectOption("explicit:false");
        await ready();
        if (!await page.locator(".save").isEnabled()) {
            await page.locator(".rule-row select").selectOption("explicit:true");
            await ready();
        }
        await page.locator(".save").click();
        await page.getByRole("button", {name: "Save 0 changes"}).waitFor();
        assert(await sourceText() === source, "Save must preserve source");
        await page.route("**/api/preview", route => route.fulfill({status: 500, contentType: "application/json", body: JSON.stringify({message: "Test preview failure"})}));
        await paste("class Broken {");
        await page.getByRole("status").filter({hasText: "Test preview failure"}).waitFor();
        assert((await sourceText()).includes("class Broken"), "Failure must preserve editable source");
        assert(await page.locator("pre[data-diff]").getAttribute("data-background") === null, "Outdated output must suppress backgrounds");
        await page.unroute("**/api/preview");
        await paste("class Recovered {}\n");
        await ready();
        await page.getByRole("searchbox", {name: "Find a preference"}).fill("end_of_line");
        await page.locator(".rule-row select").selectOption("explicit:lf");
        await ready();
        assert((await sourceText()).includes("class Recovered"), "Identical source and output must stay visible");
        await paste("");
        await ready();
        assert(await sourceText() === "", "Empty source must remain editable");
        await paste("class C { void M(int a,int b) {} }\n");
        await ready();
        await page.setViewportSize({width: 390, height: 844});
        const bounds = await page.locator("diffs-container").evaluate(host => {
            const source = host.shadowRoot.querySelector("[data-additions]").getBoundingClientRect();
            const output = host.shadowRoot.querySelector("[data-deletions]").getBoundingClientRect();
            return {stacked: output.top >= source.bottom, overflow: document.documentElement.scrollWidth > innerWidth};
        });
        assert(bounds.stacked && !bounds.overflow, "Narrow panes must stack without page overflow");
        assert(failures.length === 0, `Browser errors: ${failures.join(", ")}`);
        assert(external.length === 0, `External requests: ${external.join(", ")}`);
        return "Preview browser regression passed: formatting, LF paste, whitespace, undo, polling, save, failure recovery, empty source, mobile, offline.";
    } finally {
        await page.unroute("**/api/preview");
        page.off("pageerror", onError);
        page.off("request", onRequest);
    }
}
