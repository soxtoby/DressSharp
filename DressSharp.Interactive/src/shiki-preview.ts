// The CLI only previews C#. Keep Shiki's full language registry and WASM out of the bundle.
export {createHighlighterCore as createHighlighter, codeToHtml, createCssVariablesTheme, getTokenStyleObject, stringifyTokenStyle} from "shiki/core";
export {createJavaScriptRegexEngine} from "shiki/engine/javascript";
export const bundledLanguages = {csharp: () => import("shiki/langs/csharp.mjs")};
export function createOnigurumaEngine(): never { throw new Error("The preview uses the JavaScript highlighter."); }
