// Adapted from Placemaker (jackwrichards, GPL-3.0). Modified by TevBenji, 2026-11: minimal TypeScript/CSS bundle, isolated output and Footbridge Builder assets; no deployment.
const path = require("path");
const webpack = require("webpack");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const { CssLintPlugin } = require("./tools/css-lint");
const { CSSPresencePlugin } = require("./tools/css-presence");
const mod = require("./mod.json");

module.exports = {
    mode: "production",
    entry: { [mod.id]: "./src/index.tsx" },
    externalsType: "window",
    externals: {
        react: "React",
        "react-dom": "ReactDOM",
        "cs2/modding": "cs2/modding",
        "cs2/api": "cs2/api",
        "cs2/bindings": "cs2/bindings",
        "cs2/l10n": "cs2/l10n",
        "cs2/ui": "cs2/ui",
        "cs2/input": "cs2/input",
        "cs2/utils": "cs2/utils",
        "cohtml/cohtml": "cohtml/cohtml",
    },
    module: {
        rules: [
            { test: /\.tsx?$/, use: "ts-loader", exclude: /node_modules/ },
            {
                test: /\.css$/,
                use: [MiniCssExtractPlugin.loader, {
                    loader: "css-loader",
                    options: { modules: { auto: true, localIdentName: "fb_[local]_[hash:base64:5]" } },
                }],
            },
            {
                test: /\.(png|jpe?g|gif|svg)$/i,
                type: "asset/resource",
                generator: { filename: "images/footbridgebuilder/[name].[contenthash:8][ext]" },
            },
        ],
    },
    resolve: { extensions: [".tsx", ".ts", ".js"] },
    output: {
        path: path.resolve(__dirname, process.env.FOOTBRIDGE_BUILDER_UI_OUT || "build"),
        filename: "[name].mjs",
        library: { type: "module" },
        publicPath: "coui://ui-mods/",
    },
    experiments: { outputModule: true },
    plugins: [
        new MiniCssExtractPlugin({ filename: "[name].css" }),
        new CSSPresencePlugin(),
        new CssLintPlugin(),
        new webpack.BannerPlugin({
            banner: `Cities: Skylines II UI Module\n\nId: ${mod.id}\nAuthor: ${mod.author}\nVersion: ${mod.version}\nDependencies: ${mod.dependencies.join(",")}`,
            entryOnly: true,
            stage: webpack.Compilation.PROCESS_ASSETS_STAGE_REPORT,
        }),
    ],
};
