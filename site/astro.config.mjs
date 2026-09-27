// @ts-check
import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";
import { jacket, paper } from "./src/styles/code-theme.mjs";

// The Docs workflow names the Pages URL and repository; locally the site serves from the root.
const repository = process.env.GITHUB_REPOSITORY;

export default defineConfig({
  site: process.env.DOCS_SITE,
  base: process.env.DOCS_BASE,
  outDir: "../dist/site",
  integrations: [
    starlight({
      title: "DressSharp",
      description: "DressSharp formats C# to the rules you choose in EditorConfig, and nothing else.",
      logo: { src: "./src/generated/dresssharp.svg" },
      favicon: "/favicon.svg",
      social: repository ? [{ icon: "github", label: "GitHub", href: `https://github.com/${repository}` }] : [],
      sidebar: [
        { label: "Guides", items: ["getting-started", "choosing-rules", "formatting-automatically"] },
        { label: "Rules", items: [{ autogenerate: { directory: "rules" } }] },
        {
          label: "Reference",
          items: ["cli", "compatibility", { label: "NuGet package", link: "https://www.nuget.org/packages/DressSharp" }],
        },
      ],
      customCss: ["./src/styles/custom.css"],
      components: {
        Hero: "./src/components/landing/Hero.astro",
        PageTitle: "./src/components/PageTitle.astro",
        SiteTitle: "./src/components/SiteTitle.astro",
        ThemeSelect: "./src/components/ThemeSelect.astro",
      },
      expressiveCode: {
        themes: [jacket, paper],
        styleOverrides: { borderRadius: "6px" },
        // Shiki has no EditorConfig grammar; its INI grammar reads the same syntax.
        shiki: { langAlias: { editorconfig: "ini" } },
      },
    }),
  ],
});
