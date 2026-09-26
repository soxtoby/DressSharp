// @ts-check
import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";

// The Docs workflow names the Pages URL and repository; locally the site serves from the root.
const repository = process.env.GITHUB_REPOSITORY;

export default defineConfig({
  site: process.env.DOCS_SITE,
  base: process.env.DOCS_BASE,
  outDir: "../artifacts/site",
  integrations: [
    starlight({
      title: "DressSharp",
      description: "DressSharp is a syntax-only, explicitly configured C# formatter distributed as one .NET tool package.",
      logo: { src: "./src/generated/dresssharp.svg" },
      favicon: "/favicon.svg",
      social: repository ? [{ icon: "github", label: "GitHub", href: `https://github.com/${repository}` }] : [],
      sidebar: [
        {
          label: "Guides",
          items: ["cli", "compatibility", { label: "NuGet package", link: "https://www.nuget.org/packages/DressSharp" }],
        },
        { label: "Rules", items: [{ autogenerate: { directory: "rules" } }] },
      ],
      customCss: ["./src/styles/custom.css"],
    }),
  ],
});
