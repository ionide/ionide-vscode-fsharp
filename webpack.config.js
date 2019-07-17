var path = require("path");

function resolve(filePath) {
  return path.join(__dirname, filePath)
}


var babelOptions = {
  presets: [
    ["env", { "modules": false,
              "targets": { "node": "current" } }]],
  plugins: ["@babel/transform-runtime"]
};

var isProduction = process.argv.indexOf("-p") >= 0;
console.log("Bundling for " + (isProduction ? "production" : "development") + "...");

module.exports = function(env) {

  var ionideExperimental = (env && env.ionideExperimental);
  var outputPath = ionideExperimental? "release-exp" : "release";
  console.log("Output path: " + outputPath);

  var compilerDefines = isProduction ? [] : ["DEBUG"];
  if (ionideExperimental) {
    compilerDefines.push("IONIDE_EXPERIMENTAL");
  }

  return {
  mode: isProduction ? "production" : "development",
  target: 'node',
  devtool: "source-map",
  entry: resolve('./src/Ionide.FSharp.fsproj'),
  output: {
    filename: 'fsharp.js',
    path: resolve('./' + outputPath),
    //library: 'IONIDEFSHARP',
    libraryTarget: 'commonjs'
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
        test: /\.fs(x|proj)?$/,
        use: {
          loader: "fable-loader",
          options: {
            babel: babelOptions,
            define: compilerDefines,
            verbose: true
          }
        }
      },
      {
        test: /\.js$/,
        exclude: /node_modules/,
        use: {
          loader: 'babel-loader',
          options: babelOptions
        },
      }
    ]
  }
};
}
