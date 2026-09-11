// Run with Playwright's run-code against a fresh interactive page with its bundled sample.
// Exercises native mouse hit testing and Pierre's overlays, including horizontal scrolling.
async page => {
    const results = [];
    for (const scrollLeft of [0, 40]) {
        for (const [line, character] of [[7, 8], [7, 23], [7, 37], [9, 20]]) {
            const target = await page.evaluate(({line, character, scrollLeft}) => {
                const root = document.querySelector("diffs-container").shadowRoot;
                root.querySelector('[role="textbox"]').focus();
                root.querySelector("[data-code][data-additions]").scrollLeft = scrollLeft;
                const row = root.querySelector(`[data-additions] [data-line="${line}"]`);
                const walker = document.createTreeWalker(row, NodeFilter.SHOW_TEXT);
                let remaining = character;
                for (let node; node = walker.nextNode();) {
                    if (remaining >= node.length) { remaining -= node.length; continue; }
                    const range = document.createRange();
                    range.setStart(node, remaining);
                    range.setEnd(node, remaining + 1);
                    const rect = range.getBoundingClientRect();
                    return {x: rect.left + 0.2, y: rect.top + rect.height / 2};
                }
                throw new Error("Sample character missing");
            }, {line, character, scrollLeft});
            await page.mouse.click(target.x, target.y);
            await page.waitForTimeout(50);
            await page.keyboard.press("Shift+ArrowRight");
            await page.waitForFunction(() => !!document.querySelector("diffs-container").shadowRoot.querySelector("[data-selection-range]"));
            const geometry = await page.evaluate(({line, character}) => {
                const root = document.querySelector("diffs-container").shadowRoot;
                const row = root.querySelector(`[data-additions] [data-line="${line}"]`);
                const walker = document.createTreeWalker(row, NodeFilter.SHOW_TEXT);
                let remaining = character;
                for (let node; node = walker.nextNode();) {
                    if (remaining >= node.length) { remaining -= node.length; continue; }
                    const range = document.createRange();
                    range.setStart(node, remaining);
                    range.setEnd(node, remaining + 1);
                    const glyph = range.getBoundingClientRect();
                    const selection = root.querySelector("[data-selection-range]")?.getBoundingClientRect();
                    const caret = root.querySelector("[data-caret]")?.getBoundingClientRect();
                    if (!selection || !caret) throw new Error("Mouse and keyboard must produce a selection and caret");
                    return {left: selection.left - glyph.left, right: selection.right - glyph.right,
                        caret: caret.left + caret.width / 2 - glyph.right,
                        top: selection.top - row.getBoundingClientRect().top};
                }
                throw new Error("Sample character missing after selection");
            }, {line, character});
            if (Object.values(geometry).some(delta => Math.abs(delta) > 0.5))
                throw new Error(`Misaligned line ${line}, column ${character + 1}, scroll ${scrollLeft}: ${JSON.stringify(geometry)}`);
            results.push({line, column: character + 1, scrollLeft, ...geometry});
        }
    }
    return results;
}
