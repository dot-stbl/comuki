/**
 * Compile the single-file `comuki` binary.
 *
 * Runs `Bun.build` instead of the bare `bun build` CLI for one reason:
 * ink@5 statically traces `react-devtools-core` (a DEV-only dynamic
 * import in its reconciler). `--external` keeps the require alive and a
 * compiled binary then dies on startup looking for the package — so the
 * build shims it with an empty module instead. The real import never
 * executes (`process.env.DEV !== "true"`), the shim only satisfies the
 * bundler.
 */
const stubReactDevtools: import("bun").BunPlugin = {
  name: "stub-react-devtools-core",
  setup(builder) {
    builder.onResolve({ filter: /^react-devtools-core$/ }, () => ({
      path: "stub:react-devtools-core",
      namespace: "stub",
    }))
    builder.onLoad({ filter: /.*/, namespace: "stub" }, () => ({
      contents: "export default {};",
      loader: "js",
    }))
  },
}

const result = await Bun.build({
  entrypoints: ["bin/comuki.ts"],
  target: "bun",
  compile: true,
  outfile: "comuki",
  plugins: [stubReactDevtools],
})

if (!result.success) {
  for (const message of result.logs) {
    console.error(message)
  }
  process.exit(1)
}
