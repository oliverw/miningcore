var webpack = require("webpack");
var path = require('path');
var StatsWriterPlugin = require("webpack-stats-plugin").StatsWriterPlugin;

module.exports = {
    entry: {
        head: './app/head.ts',
        main: './app/main.ts',
        //app: './app/app.ts'
    },

    output: {
        path: path.join(__dirname, 'wwwroot', 'build'),
        filename: "[name].js",
        chunkFilename: "[chunkhash].js",
        publicPath: "http://localhost:56000/build/"
    },

    performance: {
        hints: false
    },

    // Config for minimal console.log mess
    stats: {
        assets: false,
        colors: true,
        version: true,
        hash: false,
        timings: false,
        chunks: true,
        chunkModules: false
    },

    devtool: 'source-map',

    resolve: {
        extensions: ['.js', '.ts'],
        modules: ["node_modules"],
		alias: {
		    'vue$': 'vue/dist/vue.common.js'
		}
    },

    module: {
        rules: [
            { test: /\.ts$/, loaders: ['awesome-typescript-loader'], exclude: ["node_modules", "typings"] },
            { test: /\.html$/, loader: 'html-loader', exclude: [path.resolve(__dirname, "app/Components")], },
            { test: /\.json$/, loader: 'json-loader' },

            // Component Templates
            { test: /\.html$/, include: [path.resolve(__dirname, "app/Components")], loaders: ["to-string-loader", "html-loader"] },

            // Other styles
            { test: /\.s?css$/, exclude: [path.resolve(__dirname, "app/Components")], loaders: ['style-loader', 'css-loader',
                { loader: 'postcss-loader',
                    options: {
                      plugins: [require('autoprefixer')]
                    },
                }, 'sass-loader?' +
                // OW: This is a workaround for this sass-loader issue: https://github.com/jtangelder/sass-loader/issues/59
                '&includePaths[]=' + path.resolve(__dirname, 'node_modules', 'bootstrap', 'scss')
            ]},

            { test: /\.png$/, loader: "url-loader?limit=8192" },
            { test: /\.jpg$/, loader: "file-loader" },

            { test: /\.woff(2)?(\?v=[0-9]\.[0-9]\.[0-9])?$/, loader: "url-loader?limit=10000&minetype=application/font-woff" },
            { test: /\.(ttf|eot|svg)(\?v=[0-9]\.[0-9]\.[0-9])?$/, loader: "file-loader" }                    ],
    },

    plugins: [
        new webpack.IgnorePlugin(/^\.\/locale$/, [/moment$/]),

        new webpack.DefinePlugin({
            PRODUCTION: false
        }),

        new StatsWriterPlugin({
            filename: "_wpstats.json",
            fields: ["chunks"]
        }),
    ],

    devServer: {
        historyApiFallback: false,
        inline: true,

        // Config for minimal console.log mess
        noInfo: false,
        stats: {
            assets: false,
            colors: true,
            version: true,
            hash: false,
            timings: false,
            chunks: true,
            chunkModules: false
        },

        hot: false,
        contentBase: "./wwwroot/build/",
        publicPath: "/build/",
        headers: { "X-Custom-Header": "yes" }
    }
}
