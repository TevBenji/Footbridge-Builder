// Adapted from Placemaker (jackwrichards, GPL-3.0), which follows the official Cities: Skylines II UI mod template.
// Modified by TevBenji, 2026-11: patch the last export of the module and fail the build when it cannot be patched.
// The game loads <mod id>.css next to the module only when the module exports hasCSS = true; without it the panel
// renders with no styles at all.
const { Compilation, sources } = require("webpack");

exports.CSSPresencePlugin = class CSSPresencePlugin {
    apply(compiler) {
        compiler.hooks.compilation.tap("CSSPresencePlugin", compilation => {
            compilation.hooks.processAssets.tap(
                { name: "CSSPresencePlugin", stage: Compilation.PROCESS_ASSETS_STAGE_ADDITIONS },
                () => {
                    const hasCSS = Object.keys(compilation.assets).some(asset => asset.endsWith(".css"));
                    for (const chunk of compilation.chunks) {
                        for (const file of chunk.files) {
                            if (!file.endsWith(".mjs")) continue;
                            const source = compilation.getAsset(file).source.source().toString();
                            const lastExport = /export\s*\{(?![\s\S]*export\s*\{)/;
                            if (!lastExport.test(source)) {
                                compilation.errors.push(new Error(`CSSPresencePlugin: no export list in ${file}, so the game would not load the CSS.`));
                                continue;
                            }
                            compilation.updateAsset(file, new sources.RawSource(
                                source.replace(lastExport, `const hasCSS = ${hasCSS}; export { hasCSS, `)));
                        }
                    }
                });
        });
    }
};
