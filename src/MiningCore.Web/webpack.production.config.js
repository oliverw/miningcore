var webpack = require("webpack");
var path = require('path');

var ExtractTextPlugin = require('extract-text-webpack-plugin');
var CleanWebpackPlugin = require('clean-webpack-plugin');
var extractCSS = new ExtractTextPlugin('[name].css');
var StatsWriterPlugin = require("webpack-stats-plugin").StatsWriterPlugin;

module.exports = {
    entry: {
        head: './app/head.ts',
        main: './app/main.ts',
        //app: './app/app.ts'
    },

    output: {
        path: "./wwwroot/build",
        filename: "[name].js",
        chunkFilename: "[chunkhash].js",
        publicPath: "/build/"
    },

    resolve: {
        extensions: ['.js', '.ts'],
        modules: ["node_modules"],
		alias: {
		    'vue$': 'vue/dist/vue.common.js'
		}
    },

    module: {
        loaders: [
            { test: /\.ts$/, loaders: ['awesome-typescript-loader'], exclude: ["node_modules", "typings"] },
            { test: /\.html$/, loader: 'html-loader', exclude: [path.resolve(__dirname, "app/Components")],  },
            { test: /\.json$/, loader: 'json-loader' },

            // Component Templates
            { test: /\.html$/, include: [path.resolve(__dirname, "app/Components")], loaders: ["to-string-loader", "html-loader"] },

            // Other styles
            { test: /\.s?css$/, exclude: [path.resolve(__dirname, "app/components")], loader: extractCSS.extract({    // we utilize the extracttext plugin to avoid issues with FOUC
                loader: [
                    'css-loader',
                    { loader: 'postcss-loader',
                        options: {
                            plugins: [require('autoprefixer')]
                        },
                    },
                    'sass-loader?' +
                        // OW: This is a workaround for this sass-loader issue: https://github.com/jtangelder/sass-loader/issues/59
                        '&includePaths[]=' + path.resolve(__dirname, 'node_modules', 'bootstrap', 'scss')
                ]})
            },

            { test: /\.png$/, loader: "url-loader?limit=8192" },
            { test: /\.jpg$/, loader: "file-loader" },
            { test: /\.md/, loader: 'markdown-it' },
            { test: /\.woff(2)?(\?v=[0-9]\.[0-9]\.[0-9])?$/, loader: "url-loader?limit=10000&minetype=application/font-woff" },
            { test: /\.(ttf|eot|svg)(\?v=[0-9]\.[0-9]\.[0-9])?$/, loader: "file-loader" }],
    },

    plugins: [
        new CleanWebpackPlugin(['./wwwroot/build'], {
            verbose: false
        }),

        // suppress bundling of bazillions of momentjs locales
        new webpack.IgnorePlugin(/^\.\/locale$/, [/moment$/]),

        extractCSS,

        new StatsWriterPlugin({
            filename: "_wpstats.json",
            fields: ["chunks"]
        }),

        new webpack.DefinePlugin({
            PRODUCTION: true,
            'process.env': {
                NODE_ENV: '"production"'
            }
        }),

        // minimize for production
        new webpack.optimize.UglifyJsPlugin({
            compress: {
                warnings: false
            }
        })
    ]
}
