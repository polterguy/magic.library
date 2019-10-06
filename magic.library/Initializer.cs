﻿/*
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

            // Making sure the folder for dynamic files exists on server.
            var rootFolder = (configuration["io:root-folder"] ?? "~/files")
                .Replace("~", Directory.GetCurrentDirectory())
                .TrimEnd('/') + "/";
            if (!Directory.Exists(rootFolder))
                Directory.CreateDirectory(rootFolder);

            // Wiring up magic.endpoint.
            services.AddTransient<IExecutor, Executor>();

            // Wiring up magic.signals.
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
        /// Initializing Swagger.
        /// </summary>
        /// <param name="app">Application builder for your application.</param>
        public static void InitializeSwagger(IApplicationBuilder app)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Magic"));
        }

        #region [ -- Private helper methods -- ]

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

        #endregion
    }
}
