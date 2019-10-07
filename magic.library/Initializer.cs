/*
 * Magic, Copyright(c) Thomas Hansen 2019 - thomas@gaiasoul.com
 * Licensed as Affero GPL unless an explicitly proprietary license has been obtained.
 */

using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using log4net;
using Swashbuckle.AspNetCore.Swagger;
using magic.io.services;
using magic.io.contracts;
using magic.signals.services;
using magic.signals.contracts;
using magic.endpoint.services;
using magic.library.internals;
using magic.endpoint.contracts;
using magic.lambda.io.contracts;
using magic.node.extensions.hyperlambda;

namespace magic.library
{
    /// <summary>
    /// Magic initialization class, to help you initialize Magic.
    /// </summary>
    public static class Initializer
    {
        /// <summary>
        /// Initializes log4net by loading the configuration from the specified
        /// XML log4net configuration file.
        /// </summary>
        /// <param name="configurationFile">Path to your log4net XML
        /// configuration file.</param>
        public static void InitializeLog4net(string configurationFile)
        {
            var log4netConfig = new XmlDocument();
            log4netConfig.Load(File.OpenRead(configurationFile));

            var repo = LogManager.CreateRepository(
                Assembly.GetEntryAssembly(),
                typeof(log4net.Repository.Hierarchy.Hierarchy));

            log4net.Config.XmlConfigurator.Configure(repo, log4netConfig["log4net"]);

            var logger = LogManager.GetLogger(typeof(Initializer));
            logger.Info("Initializing log4net");
        }

        /// <summary>
        /// Wires up all Magic services, using their default service
        /// implementation.
        /// </summary>
        /// <param name="configuration">Configuration for your application.</param>
        /// <param name="services">Service collection to add services into.</param>
        public static void InitializeServices(
            IConfiguration configuration,
            IServiceCollection services)
        {
            // Wiring up magic.io.
            services.AddTransient<IFileService, FileService>();
            services.AddSingleton<IAuthorize>((svc) => new AuthorizeOnlyRoles("root", "admin"));

            // Wiring up magic.lambda.io.
            services.AddTransient<IRootResolver, RootResolver>();

            // Making sure the folder for dynamic files exists on server.
            var rootFolder = (configuration["io:root-folder"] ?? "~/files")
                .Replace("~", Directory.GetCurrentDirectory())
                .TrimEnd('/') + "/";

            // Ensuring root folder for dynamica files exists on server.
            if (!Directory.Exists(rootFolder))
                Directory.CreateDirectory(rootFolder);

            // Wiring up magic.endpoint.
            services.AddTransient<IExecutor, Executor>();

            // Wiring up magic.signals.
            LoadAssemblies();
            services.AddTransient<ISignaler, Signaler>();
            services.AddSingleton<ISignalsProvider>(new SignalsProvider(Slots(services)));

            // Parts of Magic depends upon having access to the IHttpContextAccessor.
            services.AddHttpContextAccessor();
        }

        /// <summary>
        /// Initializing Swagger to create Web API documentation dynamically.
        /// </summary>
        /// <param name="services">Service collection.</param>
        public static void InitializeSwagger(IServiceCollection services)
        {
            services.AddSwaggerGen(swag =>
            {
                swag.SwaggerDoc("v1", new Info
                {
                    Title = "Magic",
                    Version = "v1",
                    Description = "An ASP.NET Web API simplification",
                    License = new License
                    {
                        Name = "Affero GPL + Proprietary commercial (Closed Source) for a fee",
                        Url = "https://polterguy.github.io",
                    },
                    Contact = new Contact
                    {
                        Name = "Thomas Hansen",
                        Email = "thomas@gaiasoul.com",
                        Url = "https://polterguy.github.io",
                    },
                });
                foreach (var idxFile in Directory.GetFiles(AppContext.BaseDirectory, "*.xml"))
                {
                    swag.IncludeXmlComments(idxFile);
                }
                swag.OperationFilter<FileUploadOperation>();
                swag.DocumentFilter<DynamicEndpointOperation>();
            });
        }

        /// <summary>
        /// Initializing application builder.
        /// </summary>
        /// <param name="app">Application builder for your application.</param>
        public static void InitalizeApp(IApplicationBuilder app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Magic"));

            // Evaluating all startup files.
            var configuration = app.ApplicationServices.GetService<IConfiguration>();
            var signaler = app.ApplicationServices.GetService<ISignaler>();
            var rootFolder = (configuration["io:root-folder"] ?? "~/files")
                .Replace("~", Directory.GetCurrentDirectory())
                .TrimEnd('/') + "/";
            foreach (var idxModules in Directory.GetDirectories(rootFolder + "modules/"))
            {
                foreach (var idxModuleFolder in Directory.GetDirectories(idxModules))
                {
                    var folder = new DirectoryInfo(idxModuleFolder);
                    if (folder.Name == "magic.startup")
                        ExecuteStartupFiles(signaler, idxModuleFolder);
                }
            }
        }

        #region [ -- Private helper methods -- ]

        static void ExecuteStartupFiles(ISignaler signaler, string folder)
        {
            // Startup folder, now executing all Hyperlambda files inside of it.
            foreach (var idxFile in Directory.GetFiles(folder, "*.hl"))
            {
                using (var stream = File.OpenRead(idxFile))
                {
                    var lambda = new Parser(stream).Lambda();
                    signaler.Signal("eval", lambda);
                }
            }

            // Recursively checking sub folders.
            foreach (var idxFolder in Directory.GetDirectories(folder))
            {
                ExecuteStartupFiles(signaler, idxFolder);
            }
        }

        /*
         * Finds all types in AppDomain that implements ISlot and that is not
         * abstract. Adds all these as transient services, and returns all of
         * them to caller.
         */
        static IEnumerable<Type> Slots(IServiceCollection services)
        {
            var type = typeof(ISlot);
            var result = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => type.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);

            foreach (var idx in result)
            {
                services.AddTransient(idx);
            }
            return result;
        }

        /*
         * Helper method to load up all assemblies into AppDomain, such that
         * we have access to all assemblies handling signals.
         */
        static void LoadAssemblies()
        {
            /*
             * Unfortunately, we have to touch every assembly that we're only
             * using slots from, and not directly referencing, since otherwise
             * the .Net compiler will optimize these assemblies completely away
             * for us, and no slots will be registered from these assemblies,
             * or any other code executing either.
             */
            var type01 = typeof(lambda.Eval);
            var type02 = typeof(lambda.auth.CreateTicket);
            var type03 = typeof(lambda.config.ConfigGet);
            var type04 = typeof(lambda.crypto.Hash);
            var type05 = typeof(lambda.http.HttpDelete);
            var type06 = typeof(lambda.hyperlambda.Hyper);
            var type07 = typeof(lambda.json.FromLambda);
            var type08 = typeof(lambda.logging.LogDebug);
            var type09 = typeof(lambda.math.Addition);
            var type10 = typeof(lambda.mssql.Connect);
            var type11 = typeof(lambda.mysql.Connect);
            var type12 = typeof(lambda.slots.SlotsCreate);
            var type13 = typeof(lambda.strings.Concat);
        }

        #endregion
    }
}
