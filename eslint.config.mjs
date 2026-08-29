import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
    // Generated and vendored assets we don't author:
    "public/wasm/**", // MediaPipe runtime, copied by `npm run tryon:assets`
    "src/generated/**",

    // The ASP.NET port. It has its own toolchain and its own vendored copy of
    // the MediaPipe runtime; linting it here reported 2,429 problems that
    // belong to neither project and drowned out anything real.
    "dotnet/**",
    "_backup_nextjs_*/**",
  ]),
]);

export default eslintConfig;
