/*
 * Magic, Copyright(c) Thomas Hansen 2019, thomas@gaiasoul.com, all rights reserved.
 * See the enclosed LICENSE file for details.
 */

using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using log4net;
using Newtonsoft.Json.Linq;
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
using magic.endpoint.services.utilities;

namespace magic.library
{
    /// <summary>
    /// Magic initialization class, to help you initialize Magic with
    /// some sane defaults.
    /// </summary>
    public static class Initializer
    {
        static ILog _logger;

        /// <summary>
        /// Convenience method that wires up all Magic components with their
        /// default settings.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">The configuration for your app.</param>
        /// <param name="licenseKey">The license key associated with
        /// your server.</param>
        public static void AddMagic(
            this IServiceCollection services,
            IConfiguration configuration,
            string licenseKey = null)
        {
            services.AddMagicLog4netServices();
            services.AddMagicFileServices(configuration);
            services.AddMagicAuthorization(configuration);
            services.AddMagicSignals(licenseKey);
            services.AddMagicEndpoints(configuration);
        }

        /// <summary>
        /// Making sure Magic is using log4net as its logger implementation.
        /// Notice, this method depends upon a "log4net.config" configuration
        /// file existing on root on your disc.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicLog4netServices(this IServiceCollection services)
        {
            /*
             * Assuming "log4net.config" exists on root folder of application.
             */
            var configurationFile = string.Concat(
                AppDomain.CurrentDomain.BaseDirectory,
                "log4net.config");

            /*
             * Loading "log4net.config" as an XML file, and using it to
             * configure log4net.
             */
            var log4netConfig = new XmlDocument();
            log4netConfig.Load(File.OpenRead(configurationFile));
            var repo = LogManager.CreateRepository(
                Assembly.GetEntryAssembly(),
                typeof(log4net.Repository.Hierarchy.Hierarchy));
            log4net.Config.XmlConfigurator.Configure(repo, log4netConfig["log4net"]);

            /*
             * Logging the fact that log4net was successfully wired up.
             */
            _logger = LogManager.GetLogger(typeof(Initializer));
            _logger.Info("Initializing log4net for Magic");

            /*
             * Associating magic.lambda.logging's ILog service contract with
             * our internal "Logger" class, which is the class actually logging
             * entries, when for instance the [log.info] slot is invoked.
             */
            services.AddTransient<lambda.logging.ILog, Logger>();
        }

        /// <summary>
        /// Wires up magic.io to use the default file service, and the
        /// AuthorizeOnlyRole authorization scheme, to only allow the "root"
        /// role to read and write files from your server using magic.io.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">The configuration for your app.</param>
        /// <param name="ensureFolder">If true, will automatically create
        /// the Magic folder where Magic stores dynamically created files.</param>
        public static void AddMagicFileServices(
            this IServiceCollection services,
            IConfiguration configuration,
            bool ensureFolder = false)
        {
            /*
             * Logging.
             */
            _logger?.Info("Using default magic.io services, and only allowing 'root' to use magic.io");

            /*
             * Associating the IFileServices with its default implementation.
             */
            services.AddTransient<IFileService, FileService>();

            /*
             * Making sure magic.io can only be used by "root" roles.
             */
            services.AddSingleton<IAuthorize>((svc) => new AuthorizeOnlyRoles("root"));

            /*
             * Associating the root folder resolver with our own internal class,
             * that resolves the root folder for magic.io to be the config
             * setting "magic:io:root-folder", or if not given "/files".
             */
            services.AddTransient<IRootResolver, RootResolver>();

            /*
             * Checking if caller wants us to automatically create folder for
             * dynamic files if it doesn't already exist.
             */
            if (ensureFolder)
            {
                // Making sure the folder for dynamic files exists on server.
                var rootFolder = (configuration["magic:io:root-folder"] ?? "~/files")
                    .Replace("~", Directory.GetCurrentDirectory())
                    .TrimEnd('/') + "/";

                // Ensuring root folder for dynamic files exists on server.
                if (!Directory.Exists(rootFolder))
                    Directory.CreateDirectory(rootFolder);
            }
        }

        /// <summary>
        /// Configures magic.lambda.auth and turning on authentication
        /// and authorization.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">The configuration for your app.</param>
        public static void AddMagicAuthorization(this IServiceCollection services, IConfiguration configuration)
        {
            _logger?.Info("Configures magic.lambda.auth to use its default ticket provider");

            /*
             * Configures magic.lambda.auth to use its default implementation of
             * HttpTicketFactory as its authentication and authorization
             * implementation.
             */
            services.AddTransient<ITicketProvider, HttpTicketProvider>();

            /*
             * Parts of Magic depends upon having access to
             * the IHttpContextAccessor,more speifically the above
             * HttpTicketProvider service implementation.
             */
            services.AddHttpContextAccessor();

            /*
             * Retrieving secret from configuration file, and wiring up
             * authentication to use JWT Bearer tokens.
             */
            var secret = configuration["magic:auth:secret"] ??
                throw new ApplicationException("Couldn't find any 'magic:auth:secret' configuration settings in your appSettings.json file. Without this configuration setting, magic can never be secure.");
            var key = Encoding.ASCII.GetBytes(secret);

            /*
             * Wiring up .Net Core to use JWT Bearer tokens for auth.
             */
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
        /// Configures magic.endpoint to use its default service implementation.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicEndpoints(this IServiceCollection services, IConfiguration configuration)
        {
            _logger?.Info("Configures magic.endpoint to use its default executor");

            /*
             * Figuring out which folder to resolve dynamic Hyperlambda files from,
             * and making sure we configure the Hyperlambda resolver to use the correct
             * folder.
             */
            var rootFolder = configuration["magic:endpoint:root-folder"] ?? "~/files/";
            rootFolder = rootFolder
                .Replace("~", Directory.GetCurrentDirectory())
                .Replace("\\", "/");
            Utilities.RootFolder = rootFolder;

            // Configuring the default executor to execute dynamic URLs.
            services.AddTransient<IExecutorAsync>(svc => new ExecutorAsync(svc.GetRequiredService<ISignaler>()));
        }

        /// <summary>
        /// Configures magic.signals such that you can signal slots.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="licenseKey">The license key associated with
        /// your installation.</param>
        public static void AddMagicSignals(this IServiceCollection services, string licenseKey = null)
        {
            /*
             * Unfortunately, we have to touch every assembly that we're only
             * using slots from, and not directly referencing, since otherwise
             * the .Net compiler will optimize these assemblies completely away
             * for us, and no slots will be registered from these assemblies,
             * or any other code executing from them either.
             *
             * Darn it Microsoft!
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
                typeof(io.controller.FilesController),
                typeof(ExecutorAsync)
            };

            /*
             * Making sure we log every assembly we touch, if log4net has
             * been configured.
             */
            if (_logger != null)
            {
                foreach (var idx in types)
                {
                    _logger.Info($"Touching '{idx.Assembly.FullName}'");
                }
            }

            /*
             * Using the default ISignalsProvider and the default ISignals
             * implementation.
             */
            _logger?.Info("Configuring magic.signals to use its default service implementations");
            services.AddTransient<ISignaler, Signaler>();
            services.AddSingleton<ISignalsProvider>(new SignalsProvider(Slots(services)));

            /*
             * Checking if caller supplied a license key.
             */
            if (!string.IsNullOrEmpty(licenseKey))
                Signaler.LicenseKey = licenseKey;
        }

        /// <summary>
        /// Convenience method to make sure you use all Magic features.
        /// </summary>
        /// <param name="app">The application builder of your app.</param>
        /// <param name="configuration">The configuration for your app.</param>
        public static void UseMagic(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            app.UseMagicExceptions();
            app.UseMagicStartupFiles(configuration);
        }

        /// <summary>
        /// Traps all unhandled exceptions, logs them to your log files using
        /// log4net (if configured), and returns the exception message back
        /// to the client as a JSON response.
        /// Notice, you'd probably not want to use this feature in production.
        /// </summary>
        /// <param name="app">The application builder of your app.</param>
        public static void UseMagicExceptions(this IApplicationBuilder app)
        {
            /*
             * Making sure we're storing errors into our log file, and that
             * we're able to return the exception message to client.
             */
            app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                var ex = context.Features.Get<IExceptionHandlerPathFeature>();
                if (ex != null)
                {
                    var msg = ex.Error.Message ?? ex.GetType().FullName;
                    if (_logger != null)
                    {
                        _logger.Error("At path: " + ex.Path);
                        _logger.Error(msg, ex.Error);
                    }
                    var response = new JObject
                    {
                        ["message"] = msg,
                        ["stack-trace"] = ex.Error.StackTrace,
                    };
                    await context.Response.WriteAsync(
                        response.ToString(Newtonsoft.Json.Formatting.Indented));
                }
                else
                {
                    var response = new JObject
                    {
                        ["message"] = "Unknown error",
                    };
                    await context.Response.WriteAsync(
                        response.ToString(Newtonsoft.Json.Formatting.Indented));
                }
            }));
        }

        /// <summary>
        /// Evaluates all Magic Hyperlambda module startup files, that are
        /// inside Magic's dynamic "modules/xxx/magic.startup/" folder, 
        /// </summary>
        /// <param name="app"></param>
        /// <param name="configuration"></param>
        public static void UseMagicStartupFiles(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            // Evaluating all startup files.
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

        /// <summary>
        /// Initializing application builder.
        /// </summary>
        /// <param name="app">Application builder for your application.</param>
        public static void InitalizeApp(IApplicationBuilder app)
        {
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
         * these types to caller.
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

        #endregion
    }
}
