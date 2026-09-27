// The code themes, as VS Code themes for Expressive Code. Starlight shows the dark one in the dark site theme and the
// light one in the light site theme. Each carries its own frame and diff colours, so they follow the switch too.

// The jacket: the colours of the landing page's code panes.
export const jacket = {
  name: "dresssharp-jacket",
  type: "dark",
  colors: {
    "editor.background": "#1f2025",
    "editor.foreground": "#e9e7e1",
  },
  tokenColors: [
    { settings: { foreground: "#e9e7e1" } },
    { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#8a8b93", fontStyle: "italic" } },
    { scope: ["keyword", "storage", "keyword.other.definition.ini"], settings: { foreground: "#d8b458" } },
    { scope: ["keyword.operator", "punctuation"], settings: { foreground: "#9a9ba3" } },
    { scope: ["entity.name.type", "entity.name.section", "support.type", "support.class"], settings: { foreground: "#f0dfae" } },
    { scope: ["entity.name.type.namespace"], settings: { foreground: "#e9e7e1" } },
    { scope: ["entity.name.function", "support.function"], settings: { foreground: "#a9c8ea" } },
    // A local or parameter is one colour where it is declared and where it is used; a member it reaches is another.
    {
      scope: ["variable.parameter", "entity.name.variable.parameter", "entity.name.variable.local", "variable.other.readwrite", "variable.other.object"],
      settings: { foreground: "#e9e7e1" },
    },
    {
      scope: ["variable.other.object.property", "variable.other.property", "entity.name.variable.field", "entity.name.variable.property"],
      settings: { foreground: "#cbb8e6" },
    },
    { scope: ["variable.language"], settings: { foreground: "#d8b458" } },
    { scope: ["string", "punctuation.definition.string"], settings: { foreground: "#9fd3c7" } },
    { scope: ["constant", "constant.character.escape"], settings: { foreground: "#e8a0aa" } },
    { scope: ["punctuation.definition.interpolation", "punctuation.section.interpolation"], settings: { foreground: "#d8b458" } },
  ],
  styleOverrides: {
    borderColor: "#3a3b42",
    frames: {
      editorTabBarBackground: "#26272d",
      editorTabBarBorderBottomColor: "#3a3b42",
      editorActiveTabBackground: "#1f2025",
      editorActiveTabForeground: "#f5f5f3",
      editorActiveTabIndicatorTopColor: "#d8b458",
      editorActiveTabIndicatorBottomColor: "transparent",
      terminalTitlebarBackground: "#26272d",
      terminalTitlebarBorderBottomColor: "#3a3b42",
      terminalBackground: "#1f2025",
      inlineButtonForeground: "#c9c9cf",
      frameBoxShadowCssValue: "0 8px 20px rgba(28, 28, 32, .14)",
    },
    textMarkers: {
      insBackground: "rgba(111, 191, 165, .14)",
      insBorderColor: "rgba(111, 191, 165, .55)",
      insDiffIndicatorColor: "#8fd0b9",
      delBackground: "rgba(214, 92, 112, .16)",
      delBorderColor: "rgba(214, 92, 112, .55)",
      delDiffIndicatorColor: "#e8a0aa",
    },
  },
};

// The paper: the same roles on the pattern paper, with burgundy chalk where the jacket has gold.
export const paper = {
  name: "dresssharp-paper",
  type: "light",
  colors: {
    "editor.background": "#fcfbf8",
    "editor.foreground": "#2a2b31",
  },
  tokenColors: [
    { settings: { foreground: "#2a2b31" } },
    { scope: ["comment", "punctuation.definition.comment"], settings: { foreground: "#7d7b75", fontStyle: "italic" } },
    { scope: ["keyword", "storage", "keyword.other.definition.ini"], settings: { foreground: "#7d1f2f" } },
    { scope: ["keyword.operator", "punctuation"], settings: { foreground: "#5e5e66" } },
    { scope: ["entity.name.type", "entity.name.section", "support.type", "support.class"], settings: { foreground: "#80590f" } },
    { scope: ["entity.name.type.namespace"], settings: { foreground: "#2a2b31" } },
    { scope: ["entity.name.function", "support.function"], settings: { foreground: "#2b5283" } },
    // A local or parameter is one colour where it is declared and where it is used; a member it reaches is another.
    {
      scope: ["variable.parameter", "entity.name.variable.parameter", "entity.name.variable.local", "variable.other.readwrite", "variable.other.object"],
      settings: { foreground: "#2a2b31" },
    },
    {
      scope: ["variable.other.object.property", "variable.other.property", "entity.name.variable.field", "entity.name.variable.property"],
      settings: { foreground: "#6b3d8f" },
    },
    { scope: ["variable.language"], settings: { foreground: "#7d1f2f" } },
    { scope: ["string", "punctuation.definition.string"], settings: { foreground: "#1d6b5f" } },
    { scope: ["constant", "constant.character.escape"], settings: { foreground: "#a8374f" } },
    { scope: ["punctuation.definition.interpolation", "punctuation.section.interpolation"], settings: { foreground: "#7d1f2f" } },
  ],
  styleOverrides: {
    borderColor: "#ddd8cc",
    frames: {
      editorTabBarBackground: "#efece4",
      editorTabBarBorderBottomColor: "#ddd8cc",
      editorActiveTabBackground: "#fcfbf8",
      editorActiveTabForeground: "#1c1c20",
      editorActiveTabIndicatorTopColor: "#7d1f2f",
      editorActiveTabIndicatorBottomColor: "transparent",
      terminalTitlebarBackground: "#efece4",
      terminalTitlebarBorderBottomColor: "#ddd8cc",
      terminalBackground: "#fcfbf8",
      inlineButtonForeground: "#5e5e66",
      frameBoxShadowCssValue: "0 6px 16px rgba(28, 28, 32, .06)",
    },
    textMarkers: {
      insBackground: "rgba(29, 107, 95, .1)",
      insBorderColor: "rgba(29, 107, 95, .45)",
      insDiffIndicatorColor: "#1d6b5f",
      delBackground: "rgba(125, 31, 47, .08)",
      delBorderColor: "rgba(125, 31, 47, .4)",
      delDiffIndicatorColor: "#7d1f2f",
    },
  },
};
