﻿/*
 * Magic Cloud, copyright Aista, Ltd. See the attached LICENSE file for details.
 */

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using magic.node.services;
using magic.node.contracts;
using magic.node.extensions;
using magic.lambda.threading;
using magic.signals.services;
using magic.signals.contracts;
using magic.endpoint.services;
using magic.library.internals;
using magic.endpoint.contracts;
using magic.lambda.http.services;
using magic.lambda.auth.services;
using magic.lambda.mime.services;
using magic.lambda.http.contracts;
using magic.lambda.auth.contracts;
using magic.lambda.mime.contracts;
using magic.lambda.caching.helpers;
using magic.lambda.logging.helpers;
using magic.lambda.scheduler.utilities;
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
        /// <summary>
        /// Convenience method that wires up all Magic components with their
        /// default settings.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">The configuration for your app.</param>
        public static void AddMagic(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddCaching();
            services.AddMagicHttp();
            services.AddMagicLogging();
            services.AddMagicSignals();
            services.AddMagicExceptions();
            services.AddMagicEndpoints(configuration);
            services.AddMagicFileServices();
            services.AddMagicAuthorization(configuration);
            services.AddMagicScheduler();
            services.AddMagicMail();
            services.AddLambda();
            services.AddSockets(configuration);
        }

        /// <summary>
        /// Adds the Magic sockets parts to your service collection.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">Needed to check if sockets are enabled in backend.</param>
        public static void AddSockets(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            if (configuration["magic:sockets:url"] != null)
            {
                services.AddSingleton<IUserIdProvider, NameUserIdProvider>();
                services.AddSignalR();
            }
        }

        /// <summary>
        /// Adds the Magic caching parts to your service collection.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddCaching(this IServiceCollection services)
        {
            services.AddSingleton<IMagicMemoryCache, MagicMemoryCache>();
        }

        /// <summary>
        /// Adds the Magic Lambda library parts to your service collection.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddLambda(this IServiceCollection services)
        {
            services.AddSingleton(typeof(ThreadRunner));
        }

        /// <summary>
        /// Adds the Magic Scheduler to your application
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicScheduler(this IServiceCollection services)
        {
            services.AddSingleton(
                typeof(IScheduler),
                svc => new Scheduler(svc, new Logger(svc.GetService<ISignaler>())));
        }

        /// <summary>
        /// Tying up audit logging for Magic.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicLogging(this IServiceCollection services)
        {
            /*
             * Associating magic.lambda.logging's ILogger service contract with
             * our internal "Logger" class, which is the class actually logging
             * entries, when for instance the [log.info] slot is invoked.
             */
            services.AddTransient<ILogger, Logger>();
        }

        /// <summary>
        /// Making sure Magic is able to invoke HTTP REST endpoints.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicHttp(this IServiceCollection services)
        {
            services.AddHttpClient();
            services.AddTransient<IMagicHttp, MagicHttp>();
        }

        /// <summary>
        /// Wires up magic.lambda.io to use the default file, folder and stream service.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicFileServices(this IServiceCollection services)
        {
            /*
             * Associating the IFileServices, IFolderService and IStreamService with its default implementation.
             */
            services.AddTransient<IFileService, FileService>();
            services.AddTransient<IFolderService, FolderService>();
            services.AddTransient<IStreamService, StreamService>();

            /*
             * Associating the root folder resolver with our own internal class,
             * that resolves the root folder for magic.lambda.io to be the config
             * setting "magic:io:root-folder", or if not given "~/files".
             */
            services.AddTransient<IRootResolver, RootResolver>();
        }

        /// <summary>
        /// Configures magic.lambda.auth and turning on authentication
        /// and authorization.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">The configuration for your app.</param>
        public static void AddMagicAuthorization(this IServiceCollection services, IConfiguration configuration)
        {
            /*
             * Configures magic.lambda.auth to use its default implementation of
             * HttpTicketFactory as its authentication and authorization
             * implementation.
             */
            services.AddTransient<ITicketProvider, HttpTicketProvider>();

            /*
             * Parts of Magic depends upon having access to
             * the IHttpContextAccessor, more speifically the above
             * HttpTicketProvider service implementation.
             */
            services.AddHttpContextAccessor();

            /*
             * Retrieving secret from configuration file, and wiring up
             * authentication to use JWT Bearer tokens.
             */
            var secret = configuration["magic:auth:secret"];
            if (string.IsNullOrEmpty(secret))
                throw new HyperlambdaException("Couldn't find any 'magic:auth:secret' configuration settings in your appSettings.json file. Magic can never be secure unless you provide this configuration setting.");

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
                x.Events = new JwtBearerEvents
                {
                    OnMessageReceived = (context) =>
                    {
                        /*
                         * If token exists in cookie, we default to using cookie instead of Authorization header.
                         * This allows individual installations to use cookies to transmit JWT tokens, which
                         * arguably is more secure.
                         *
                         * Notice, we also need to allow for sockets requests to authenticate using QUERY parameters,
                         * at which point we set token to value from 'access_token' QUERY param.
                         */
                        var cookie = context.Request.Cookies["ticket"];
                        if (!string.IsNullOrEmpty(cookie))
                            context.Token = cookie;
                        else if (context.HttpContext.Request.Path.StartsWithSegments("/sockets") && context.Request.Query.ContainsKey("access_token"))
                            context.Token = context.Request.Query["access_token"];
                        return Task.CompletedTask;
                    },
                };
                var httpsOnly = configuration["magic:auth:https-only"] ?? "false";
                x.RequireHttpsMetadata = httpsOnly.ToLowerInvariant() == "true";
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = false,

                    /*
                     * Notice, making sure we retrieve secret for each time it's needed.
                     * This will make it possible to change the secret without having to restart the web app.
                     */
                    IssuerSigningKeyResolver = (token, secToken, kid, valParams) =>
                    {
                        var key = Encoding.ASCII.GetBytes(configuration["magic:auth:secret"]);
                        return new List<SecurityKey>() { new SymmetricSecurityKey(key) };
                    }
                };
            });
        }

        /// <summary>
        /// Configures magic.endpoint to use its default service implementation.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">Your apps configuration.</param>
        public static void AddMagicEndpoints(this IServiceCollection services, IConfiguration configuration)
        {
            // Configuring the default executor to execute dynamic URLs.
            services.AddTransient<IHttpExecutorAsync, HttpExecutorAsync>();
            services.AddTransient<IHttpArgumentsHandler, HttpArgumentsHandler>();
        }

        /// <summary>
        /// Adds Magic Mail to your application
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicMail(this IServiceCollection services)
        {
            services.AddTransient<ISmtpClient, SmtpClient>();
            services.AddTransient<IPop3Client, Pop3Client>();
        }

        /// <summary>
        /// Configures magic.signals such that you can signal slots.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicSignals(this IServiceCollection services)
        {
            /*
             * Loading all assemblies that are not loaded up for some reasons.
             */
            var loadedPaths = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(x => x.Location);

            var assemblyPaths = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll");
            foreach (var idx in assemblyPaths.Where(x => !loadedPaths.Contains(x, StringComparer.InvariantCultureIgnoreCase)))
            {
                AppDomain.CurrentDomain.Load(AssemblyName.GetAssemblyName(idx));
            }

            /*
             * Using the default ISignalsProvider and the default ISignals
             * implementation.
             */
            services.AddTransient<ISignaler, Signaler>();
            services.AddSingleton<ISignalsProvider>(new SignalsProvider(Slots(services)));
        }

        /// <summary>
        /// Donfigures Magic exceptions allowing you to handle exceptions with your own "exceptions.hl" files.
        /// </summary>
        /// <param name="services">Service collection</param>
        public static void AddMagicExceptions(this IServiceCollection services)
        {
            services.AddTransient<IExceptionHandler, ExceptionHandler>();
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
            app.UseScheduler(configuration);
        }

        /// <summary>
        /// Convenience method to make sure we start scheduler if we're supposed to.
        /// </summary>
        /// <param name="app">The application builder of your app.</param>
        /// <param name="configuration">The configuration for your app.</param>
        public static void UseScheduler(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            // Starting scheduler, but only if system has been setup.
            if (configuration["magic:auth:secret"] != "THIS-IS-NOT-A-GOOD-SECRET-PLEASE-CHANGE-IT")
            {
                var scheduler = app.ApplicationServices.GetService(typeof(IScheduler)) as IScheduler;
                scheduler.StartScheduler();
            }
        }

        /// <summary>
        /// Traps all unhandled exceptions, and returns them to client,
        /// if build is DEBUG build, and/or exception allows for this.
        /// </summary>
        /// <param name="app">The application builder of your app.</param>
        public static void UseMagicExceptions(this IApplicationBuilder app)
        {
            /*
             * Making sure we're handling exceptions correctly
             * according to how installation is configured.
             */
            app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
            {
                var handler = errorApp.ApplicationServices.GetService<IExceptionHandler>();
                var rootResolver = errorApp.ApplicationServices.GetService<IRootResolver>();
                var fileService = errorApp.ApplicationServices.GetService<IFileService>();
                await handler.HandleException(errorApp, context, rootResolver, fileService);
            }));
        }

        /// <summary>
        /// Evaluates all Magic Hyperlambda module startup files, that are
        /// inside Magic's dynamic "modules/xxx/magic.startup/" folder, 
        /// </summary>
        /// <param name="app">Your application builder.</param>
        /// <param name="configuration">Your app's configuration.</param>
        public static void UseMagicStartupFiles(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            // Creating a signaler and figuring out root path for dynamic Hyperlambda files.
            var signaler = app.ApplicationServices.GetService<ISignaler>();

            // Retrieving all module folders.
            var folders = GetModuleFolders(app, configuration);

            // Iterating through all module folders to make sure we execute startup files.
            foreach (var idxModule in folders)
            {
                /*
                 * Checking if this is a "startup folder", at which point we
                 * execute all Hyperlambda files (recursively) inside of it.
                 */
                try
                {
                    if (new DirectoryInfo(idxModule).Name == "magic.startup")
                        ExecuteStartupFiles(app, signaler, idxModule);
                }
                catch (Exception err)
                {
                    // Verifying system has been configured before attempting to log error.
                    if (configuration["magic:auth:secret"] != "THIS-IS-NOT-A-GOOD-SECRET-PLEASE-CHANGE-IT")
                    {
                        try
                        {
                            var logger = app.ApplicationServices.GetService<ILogger>();
                            logger.Error($"Exception occurred as we tried to initialise module '{idxModule}', message from system was '{err.Message}'", err);
                        }
                        catch (Exception error)
                        {
                            // Nothing to do here really ...
                            Console.WriteLine(error.Message);
                        }
                    }
                }

                // Finding all folders inside of the currently iterated folder inside of "/modules/".
                foreach (var idxModuleFolder in Directory.GetDirectories(idxModule))
                {
                    /*
                     * Notice, in order to have one bogus app take down the entire app,
                     * we wrap this guy inside a try/catch block.
                     */
                    try
                    {
                        /*
                         * Checking if this is a "startup folder", at which point we
                         * execute all Hyperlambda files (recursively) inside of it.
                         */
                        var folder = new DirectoryInfo(idxModuleFolder);
                        if (folder.Name == "magic.startup")
                        {
                            ExecuteStartupFiles(app, signaler, idxModuleFolder);
                        }
                        else
                        {
                            /*
                             * Checking if there's a magic.startup folder inside of
                             * the currently iterated sub-module folder.
                             */
                            foreach (var idxSubModuleFolder in Directory
                                .GetDirectories(idxModuleFolder)
                                .Where(x => new DirectoryInfo(x).Name == "magic.startup"))
                            {
                                ExecuteStartupFiles(app, signaler, idxSubModuleFolder);
                            }
                        }
                    }
                    catch (Exception err)
                    {
                        // Verifying system has been configured before attempting to log error.
                        if (configuration["magic:auth:secret"] != "THIS-IS-NOT-A-GOOD-SECRET-PLEASE-CHANGE-IT")
                        {
                            try
                            {
                                var logger = app.ApplicationServices.GetService<ILogger>();
                                logger.Error($"Exception occurred as we tried to initialise module '{idxModule}', message from system was '{err.Message}'", err);
                            }
                            catch (Exception error)
                            {
                                // Nothing to do here really ...
                                Console.WriteLine(error.Message);
                            }
                        }
                    }
                }
            }
        }

        #region [ -- Private helper methods -- ]

        /*
         * Returns all module folders to caller.
         */
        static IEnumerable<string> GetModuleFolders(IApplicationBuilder app, IConfiguration configuration)
        {
            // Creating our services.
            var folderService = app.ApplicationServices.GetService<IFolderService>();
            var rootResolver = app.ApplicationServices.GetService<IRootResolver>();

            // Retrieving all folders inside of our "/modules/" folder.
            var system = folderService.ListFolders(rootResolver.AbsolutePath("system/"));
            var modules = folderService.ListFolders(rootResolver.AbsolutePath("modules/"));

            // Returning folders to caller.
            system.AddRange(modules);
            return system;
        }

        /*
         * Will recursively execute every single Hyperlambda file inside of
         * the specified folder.
         */
        static void ExecuteStartupFiles(IApplicationBuilder app, ISignaler signaler, string folder)
        {
            // Creating our services.
            var fileService = app.ApplicationServices.GetService<IFileService>();
            var folderService = app.ApplicationServices.GetService<IFolderService>();
            var rootResolver = app.ApplicationServices.GetService<IRootResolver>();

            // Startup folder, now executing all Hyperlambda files inside of it.
            foreach (var idxFile in fileService.ListFiles(folder, ".hl"))
            {
                var lambda = HyperlambdaParser.Parse(fileService.Load(idxFile));
                signaler.Signal("eval", lambda);
            }

            // Recursively checking sub folders.
            foreach (var idxFolder in folderService.ListFolders(folder))
            {
                ExecuteStartupFiles(app, signaler, idxFolder);
            }
        }

        /*
         * Finds all types in AppDomain that implements ISlot and that is not
         * abstract. Adds all these as transient services, and returns all of
         * these types to caller.
         */
        static IEnumerable<Type> Slots(IServiceCollection services)
        {
            var type1 = typeof(ISlot);
            var type2 = typeof(ISlotAsync);
            var result = AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => !x.IsDynamic && !x.FullName.StartsWith("Microsoft", StringComparison.InvariantCulture))
                .SelectMany(s => s.GetTypes())
                .Where(p => (type1.IsAssignableFrom(p) || type2.IsAssignableFrom(p)) && !p.IsInterface && !p.IsAbstract);

            foreach (var idx in result)
            {
                services.AddTransient(idx);
            }
            return result;
        }

        #endregion
    }
}
