//@ts-check
/** @typedef {import('webpack').Configuration} WebpackConfig **/

'use strict';

var path = require("path");

function resolve(filePath) {
  return path.join(__dirname, filePath)
}

var babelOptions = {
  presets: [
    ["@babel/preset-env", {
      "modules": false
    }]
  ],
  plugins: [
    "@babel/plugin-transform-runtime",
    "@babel/plugin-proposal-nullish-coalescing-operator"
  ]
}

module.exports = function (env, argv) {
  var isProduction = argv.mode == "production"
  console.log("Bundling for " + (isProduction ? "production" : "development") + "...");

  var ionideExperimental = (env && env.ionideExperimental);
  var outputPath = ionideExperimental ? "release-exp" : "release";
  console.log("Output path: " + outputPath);

  /**@type WebpackConfig*/
  const config = {
    target: 'node',
    mode: isProduction ? "production" : "development",
    devtool: "source-map",
    entry: './out/fsharp.js',
    output: {
      filename: 'fsharp.js',
      path: resolve('./' + outputPath),
      //library: 'IONIDEFSHARP',
      libraryTarget: 'commonjs2'
    },
    resolve: {
      modules: [resolve("./node_modules/")]
    },
    //externals: [nodeExternals()],
    externals: {
      // Who came first the host or the plugin ?
      "vscode": "commonjs vscode",

      // Optional dependencies of ws
      "utf-8-validate": "commonjs utf-8-validate",
      "bufferutil": "commonjs bufferutil"
    },
    module: {
      rules: [
        {
          test: /\.js$/,
          exclude: /node_modules/,
          use: {
            loader: 'babel-loader',
            options: babelOptions
          },
        },
        {
          test: /\.js$/,
          include: /vscode/,
          use: {
            loader: 'babel-loader',
            options: babelOptions
          },
        }
      ]
    }
  };
  return config;
}
