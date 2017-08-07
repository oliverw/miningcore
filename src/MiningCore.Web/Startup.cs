using System;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiningCore.Filters;
using MiningCore.Middlewares;
using MiningCore.Utils;
using Newtonsoft.Json.Serialization;

namespace MiningCore
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            this.env = env;

            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            if (env.IsDevelopment())
                builder.AddUserSecrets("C3E97CF7-2AA6-4834-B252-45FCAE72D233");

            Configuration = builder.Build();
        }

        private readonly IHostingEnvironment env;

        public IConfigurationRoot Configuration { get; }
        public IContainer Container { get; private set; }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;

                options.MimeTypes = new[]
                {
                    // Default
                    "text/plain",
                    "text/css",
                    "application/javascript",
                    "text/html",
                    "application/xml",
                    "text/xml",
                    "application/json",
                    "text/json",
                    // Custom
                    "image/svg+xml"
                };

                options.Providers.Add<GzipCompressionProvider>();
            });

            services.AddLocalization(options => options.ResourcesPath = "Resources");

            // Rate limiting
            services.AddResponseCompression();
            services.AddMemoryCache();

            services.AddMvc(options =>
            {
                options.Filters.Add(typeof(AddAmbientDataActionFilter));

                if (env.IsProduction())
                    options.Filters.Add(typeof(RequireHttpsAttribute));
            })
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization().AddJsonOptions(options =>
            {
                options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            });

            services.AddOptions();

            services.AddLogging();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton<WebpackStatsProvider>();
            services.Configure<AppConfig>(Configuration.GetSection("AppConfig"));
            services.AddSingleton<IConfiguration>(Configuration);

            services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

            // Create the container builder.
            var builder = new ContainerBuilder();

            builder.Populate(services);

            builder.RegisterAssemblyModules(new[]
            {
                //typeof(SqlServerDataAccess.AutofacModule).GetTypeInfo().Assembly,
                typeof(AutofacModule).GetTypeInfo().Assembly,
            });

            // AutoMapper
            //var amConf = new MapperConfiguration(cfg =>
            //{
            //    cfg.CreateMissingTypeMaps = true;

            //    cfg.AddProfile(new AutoMapperProfile());
            //    cfg.AddProfile(new SqlServerDataAccess.AutoMapperProfile());
            //});

            //builder.Register((ctx, parms) => amConf.CreateMapper());

            // Build container
            this.Container = builder.Build();

            return new AutofacServiceProvider(this.Container);
        }

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory,
            IApplicationLifetime appLifetime, IOptions<AppConfig> appConfig)
        {
            app.UseResponseCompression();

            if (env.IsDevelopment())
            {
                //loggerFactory.AddConsole(Configuration.GetSection("Logging"));
                //loggerFactory.AddDebug();
            }

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();

                // Webpack Dev Server Proxy
                var webpackProxyMiddlewareOptions = new WebpackProxyMiddlewareOptions
                {
                    IncludePaths = new [] {"/build/" }
                };

                app.UseMiddleware<WebpackProxyMiddleware>(Options.Create(webpackProxyMiddlewareOptions));
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

			app.UseStaticFiles();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Pool}/{action=Index}/{id?}");
            });

            appLifetime.ApplicationStopped.Register(() => this.Container.Dispose());
        }
    }
}
