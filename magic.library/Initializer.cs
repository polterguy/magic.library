/*
 * Magic, Copyright(c) Thomas Hansen 2019 - thomas@gaiasoul.com
 * Licensed as Affero GPL unless an explicitly proprietary license has been obtained.
 */

using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using log4net;
using magic.io.services;
using magic.io.contracts;
using magic.signals.services;
using magic.signals.contracts;
using magic.endpoint.services;
using magic.library.internals;
using magic.endpoint.contracts;
using magic.lambda.io.contracts;
using magic.lambda.auth.services;
using magic.lambda.auth.contracts;
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
            services.AddTransient<ITicketProvider, HttpTicketProvider>();

            // Wiring up magic.lambda.io.
            services.AddTransient<IRootResolver, RootResolver>();

            // Making sure the folder for dynamic files exists on server.
            var rootFolder = (configuration["magic:io:root-folder"] ?? "~/files")
                .Replace("~", Directory.GetCurrentDirectory())
                .TrimEnd('/') + "/";

            // Ensuring root folder for dynamica files exists on server.
            if (!Directory.Exists(rootFolder))
                Directory.CreateDirectory(rootFolder);

            // Wiring up magic.endpoint.
            services.AddTransient<IExecutor, Executor>();

            // Wiring up magic.lambda.logging.
            services.AddTransient<magic.lambda.logging.ILog, Logger>();

            // Wiring up magic.signals.
            LoadAssemblies();
            services.AddTransient<ISignaler, Signaler>();
            services.AddSingleton<ISignalsProvider>(new SignalsProvider(Slots(services)));

            // Parts of Magic depends upon having access to the IHttpContextAccessor.
            services.AddHttpContextAccessor();

            var secret = configuration["magic:auth:secret"];
            var key = Encoding.ASCII.GetBytes(secret);
            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                };
            });
        }

        /// <summary>
        /// Initializing application builder.
        /// </summary>
        /// <param name="app">Application builder for your application.</param>
        public static void InitalizeApp(IApplicationBuilder app)
        {
            // Evaluating all startup files.
            var configuration = app.ApplicationServices.GetService<IConfiguration>();
            var signaler = app.ApplicationServices.GetService<ISignaler>();
            var rootFolder = (configuration["magic:io:root-folder"] ?? "~/files")
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
            var types = new Type[]
            {
                typeof(lambda.Eval),
                typeof(lambda.auth.CreateTicket),
                typeof(lambda.config.ConfigGet),
                typeof(lambda.crypto.Hash),
                typeof(lambda.http.HttpDelete),
                typeof(lambda.hyperlambda.Lambda2Hyper),
                typeof(lambda.json.Json2Lambda),
                typeof(lambda.logging.LogDebug),
                typeof(lambda.math.Addition),
                typeof(lambda.mssql.Connect),
                typeof(lambda.mysql.Connect),
                typeof(lambda.slots.Create),
                typeof(lambda.strings.Concat),
                typeof(io.controller.FilesController)
            };
        }

        #endregion
    }
}
