/*
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
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using magic.data.cql.io;
using magic.node.services;
using magic.node.contracts;
using magic.lambda.sockets;
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
using magic.lambda.config.services;
using magic.lambda.caching.services;
using magic.lambda.logging.services;
using magic.lambda.logging.contracts;
using magic.lambda.caching.contracts;
using magic.lambda.scheduler.services;
using magic.lambda.scheduler.contracts;
using magic.lambda.scheduler.utilities;
using magic.node.extensions.hyperlambda;
using magic.endpoint.services.utilities;
using sig_serv = magic.signals.services;

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
        /// <param name="configuration">Configuration object.</param>
        public static void AddMagic(this IServiceCollection services, IConfiguration configuration)
        {
            // Adding global configuration to services collection.
            services.AddSingleton<IConfiguration>(svc => configuration);

            // Making sure we add all controllers in AppDomain and use Newtonsoft JSON as JSON library.
            services.AddControllers().AddNewtonsoftJson();

            // Wiring up Magic specific services.
            services.AddMagicSignals();
            services.AddMagicFileServices(configuration);
            services.AddMagicConfiguration();
            services.AddMagicCaching(configuration);
            services.AddMagicHttp(configuration);
            services.AddMagicLogging(configuration);
            services.AddMagicExceptions();
            services.AddMagicEndpoints();
            services.AddMagicAuthorization(configuration);
            services.AddMagicScheduler();
            services.AddMagicMail();
            services.AddMagicLambda();
            services.AddMagicSockets(configuration);
        }

        #region [ -- Helper methods to wire up IoC container -- ]

        /// <summary>
        /// Wires up magic.lambda.io to use the default file, folder and stream service.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">Your configuration settings.</param>
        public static void AddMagicFileServices(this IServiceCollection services, IConfiguration configuration)
        {
            /*
             * Associating the IFileServices, IFolderService, IStreamService, and IRootResolver with
             * its correct implementation according to configuration settings, defaulting to their
             * default implementations.
             */
            services.AddTransient(
                typeof(IFileService),
                GetType(configuration["magic:io:file-service"] ?? "magic.node.services.FileService"));

            services.AddTransient(
                typeof(IFolderService),
                GetType(configuration["magic:io:folder-service"] ?? "magic.node.services.FolderService"));

            services.AddTransient(
                typeof(IStreamService),
                GetType(configuration["magic:io:stream-service"] ?? "magic.node.services.StreamService"));

            services.AddTransient(
                typeof(IRootResolver),
                GetType(configuration["magic:io:root-resolver"] ?? "magic.library.internals.RootResolver"));

            // Ensuring "mixed service" has access to actual service implementations.
            services.AddTransient<CqlFileService>();
            services.AddTransient<CqlFolderService>();
            services.AddTransient<CqlStreamService>();
            services.AddTransient<FileService>();
            services.AddTransient<FolderService>();
            services.AddTransient<StreamService>();
        }

        /// <summary>
        /// Adds Magic configuration.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicConfiguration(this IServiceCollection services)
        {
            services.AddTransient<IMagicConfiguration, MagicConfiguration>();
        }

        /// <summary>
        /// Adds the Magic caching parts to your service collection.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">Configuration object.</param>
        public static void AddMagicCaching(this IServiceCollection services, IConfiguration configuration)
        {
            /*
             * Associating magic.lambda.logging's ILogger service contract with
             * whatever implementation declared in our appsettings.json file.
             */
            var type = GetType(configuration["magic:caching:service"] ?? "magic.lambda.caching.services.MagicMemoryCache");
            services.AddTransient(type);
            services.AddSingleton(typeof(IMagicCache), (svc) => svc.GetService(type));
        }

        /// <summary>
        /// Making sure Magic is able to invoke HTTP REST endpoints.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicHttp(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient();
            services.AddTransient(
                typeof(IMagicHttp),
                GetType(configuration["magic:http:service"] ?? "magic.lambda.http.services.MagicHttp"));
        }

        /// <summary>
        /// Tying up audit logging for Magic.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">Configruation object.</param>
        public static void AddMagicLogging(this IServiceCollection services, IConfiguration configuration)
        {
            /*
             * Associating magic.lambda.logging's ILogger service contract with
             * whatever implementation declared in our appsettings.json file.
             */
            services.AddTransient(
                typeof(ILogger),
                GetType(configuration["magic:logging:service"] ?? "magic.lambda.logging.services.Logger"));

            services.AddTransient(
                typeof(ILogQuery),
                GetType(configuration["magic:logging:service"] ?? "magic.lambda.logging.services.Logger"));
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
            services.AddTransient<ISignaler, sig_serv.Signaler>();
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
        /// Configures magic.endpoint to use its default service implementation.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicEndpoints(this IServiceCollection services)
        {
            // Configuring the default executor to execute dynamic URLs.
            services.AddTransient<IHttpExecutorAsync, HttpExecutorAsync>();
            services.AddTransient<IHttpArgumentsHandler, HttpArgumentsHandler>();
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
             * Checking if we should add IIS authentication scheme, which we
             * only do if configuration has declared an "auto login slot".
             */
            if (!string.IsNullOrEmpty(configuration["magic:auth:auto-auth"]))
                services.AddAuthentication(IISDefaults.AuthenticationScheme);

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
        /// Adds the Magic Scheduler to your application
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicScheduler(this IServiceCollection services)
        {
            services.AddSingleton<ITaskScheduler, Scheduler>();
            services.AddSingleton<ITaskStorage, Scheduler>();
            services.AddTransient<IServiceCreator<ISignaler>>((svc) => new ServiceCreator<ISignaler>(svc));
            services.AddTransient<IServiceCreator<ILogger>>((svc) => new ServiceCreator<ILogger>(svc));
            services.AddTransient<IServiceCreator<IMagicConfiguration>>((svc) => new ServiceCreator<IMagicConfiguration>(svc));
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
        /// Adds the Magic Lambda library parts to your service collection.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        public static void AddMagicLambda(this IServiceCollection services)
        {
            services.AddSingleton<ThreadRunner>();
        }

        /// <summary>
        /// Adds the Magic sockets parts to your service collection.
        /// </summary>
        /// <param name="services">Your service collection.</param>
        /// <param name="configuration">Needed to check if sockets are enabled in backend.</param>
        public static void AddMagicSockets(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            if (configuration["magic:sockets:url"] != null)
            {
                services.AddSingleton<IUserIdProvider, NameUserIdProvider>();
                services.AddSignalR();
            }
        }

        #endregion

        #region [ -- Helper methods to correctly wire up application builder -- ]

        /// <summary>
        /// Convenience method to make sure you use all Magic features.
        /// </summary>
        /// <param name="app">The application builder of your app.</param>
        /// <param name="configuration">The configuration for your app.</param>
        public static void UseMagic(this IApplicationBuilder app, IConfiguration configuration)
        {
            app.UseMagicExceptions();
            app.UseMagicStartupFiles(configuration);
            app.UseMagicScheduler(configuration);
            app.UseHttpsRedirection();
            app.UseMagicCors(configuration);
            app.UseAuthentication();
            app.UseRouting().UseEndpoints(conf =>
            {
                conf.MapControllers();

                /*
                 * Checking if SignalR is enabled, and if so, making sure we
                 * resolve the "/sockets" endpoint as SignalR invocations.
                 */
                var socketsUrl = configuration["magic:sockets:url"];
                if (socketsUrl != null)
                    conf.MapHub<MagicHub>(socketsUrl);
            });
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
        /// Executes all Hyperlambda module startup files that are inside Magic's dynamic folder,
        /// implying all files in all folders that have the name of "magic.startup", and can either
        /// be found on system level, or inside modules, or inside folders inside modules.
        /// 
        /// This allows us to have 3 layers of startup files; System level, module level, and
        /// sub-module level.
        /// </summary>
        /// <param name="app">Your application builder.</param>
        /// <param name="configuration">Your app's configuration.</param>
        public static void UseMagicStartupFiles(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            /*
             * Iterating through all system folders, such as "/modules/foo/" and "/system/auth/"
             */
            var folderService = app.ApplicationServices.GetService<IFolderService>();
            foreach (var idxSystemFolder in GetModuleFolders(app))
            {
                /*
                 * Checking if this folder is a "magic.startup" folder, implying e.g. "/system/magic.startup/"
                 * or "/modules/magic.startup/"
                 */
                if (new DirectoryInfo(idxSystemFolder).Name == "magic.startup")
                {
                    ExecuteStartupFilesWrapper(app, idxSystemFolder);
                }
                else
                {
                    /*
                     * Iterating through all folders that are module startup folders, implying e.g.
                     * "/system/auth/magic.startup/" or "/modules/some-module/magic.startup/".
                     */
                    foreach (var idxModuleFolder in folderService
                        .ListFolders(idxSystemFolder)
                        .Where(x => new DirectoryInfo(x).Name == "magic.startup"))
                    {
                        ExecuteStartupFilesWrapper(app, idxModuleFolder);
                    }

                    /*
                     * Iterating through all sub folders of folders that are NOT "magic.startup" folders,
                     * to see if there are sub-modules containing startup folders, such as e.g.
                     * "/modules/some-module/some-sub-module/magic.startup/".
                     */
                    foreach (var idxModuleFolder in folderService
                        .ListFolders(idxSystemFolder)
                        .Where(x => new DirectoryInfo(x).Name != "magic.startup")
                        .SelectMany(x => folderService.ListFolders(x))
                        .Where(x => x == "magic.startup"))
                    {
                        ExecuteStartupFilesWrapper(app, idxModuleFolder);
                    }
                }
            }
        }

        /// <summary>
        /// Starts the scheduler for all schedules for all tasks in Magic.
        /// </summary>
        /// <param name="app">Your application builder.</param>
        /// <param name="configuration">Your app's configuration object.</param>
        public static void UseMagicScheduler(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            if (configuration["magic:auth:secret"] != "THIS-IS-NOT-A-GOOD-SECRET-PLEASE-CHANGE-IT")
            {
                /*
                 * Starting task scheduler, passing in max concurrent jobs, defaulting to 8 if no
                 * configuration value is found.
                 */
                var maxThreads = int.Parse(configuration["magic:scheduler:max-threads"] ?? "8");
                app.ApplicationServices.GetService<ITaskScheduler>()
                    .StartAsync(maxThreads)
                    .GetAwaiter()
                    .GetResult();
            }
        }

        /// <summary>
        /// Convenience method to make sure we correctly apply CORS policy.
        /// </summary>
        /// <param name="app">The application builder of your app.</param>
        /// <param name="configuration">The configuration for your app.</param>
        public static void UseMagicCors(
            this IApplicationBuilder app,
            IConfiguration configuration)
        {
            var origins = configuration["magic:frontend:urls"];

            /*
             * Notice, we have no way to determine the frontend used unless we find an explicit configuration part.
             * Hence, we've got no other options than to simply turn on everything if no frontends are declared
             * in configuration.
             */
            if (!string.IsNullOrEmpty(origins))
                app.UseCors(x => 
                    x
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials()
                        .WithOrigins(origins.Split(',').Select(x => x.Trim()).ToArray()));
            else
                app.UseCors(x => x.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()); //NOSONAR
        }

        #endregion

        #region [ -- Private helper methods -- ]

        /*
         * Resolves the specified type and returns to caller.
         */
        static Type GetType(string name)
        {
            foreach (Assembly idxAsm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (idxAsm.FullName.StartsWith("System."))
                    continue;

                var result = idxAsm.GetType(name);
                if (result != null)
                    return result;
            }
            return null;
        }

        /*
         * Returns all module folders to caller.
         */
        static IEnumerable<string> GetModuleFolders(IApplicationBuilder app)
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
         * Method responsible for executing startup files for specified folder.
         */
        static void ExecuteStartupFilesWrapper(
            IApplicationBuilder app,
            string startupFolder)
        {
            /*
             * To avoid one module's startup files to prevent another module's startup files from
             * executing, we wrap execution of a module's startup files inside a try/catch, and
             * simply logs any errors, while still allowing execution to continue.
             */
            try
            {
                ExecuteHyperlambdaFilesRecursively(app, startupFolder);
            }
            catch (Exception err)
            {
                /*
                 * Since we're not sure if we've configured the system we've got no other options
                 * but to log this to the console.
                 */
                Console.WriteLine(err.Message);
            }
        }

        /*
         * Will recursively execute every single Hyperlambda file inside of
         * the specified folder.
         */
        static void ExecuteHyperlambdaFilesRecursively(IApplicationBuilder app, string folder)
        {
            // Creating our services.
            var fileService = app.ApplicationServices.GetService<IFileService>();
            var folderService = app.ApplicationServices.GetService<IFolderService>();
            var signaler = app.ApplicationServices.GetService<ISignaler>();

            // Startup folder, now executing all Hyperlambda files inside of it.
            foreach (var idxFile in fileService.ListFiles(folder, ".hl"))
            {
                var lambda = HyperlambdaParser.Parse(fileService.Load(idxFile));
                signaler.Signal("eval", lambda);
            }

            // Recursively checking sub folders.
            foreach (var idxFolder in folderService.ListFolders(folder))
            {
                ExecuteHyperlambdaFilesRecursively(app, idxFolder);
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
