/*
 * Magic, Copyright(c) Thomas Hansen 2019 - 2021, thomas@servergardens.com, all rights reserved.
 * See the enclosed LICENSE file for details.
 */

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using magic.node;
using magic.node.extensions;
using magic.lambda.exceptions;
using magic.signals.contracts;
using magic.lambda.logging.helpers;
using magic.endpoint.services.utilities;

namespace magic.library.internals
{
    /*
     * Internal helper class to handle unhandled exceptions.
     */
    internal sealed class ExceptionHandler : IExceptionHandler
    {
        /*
         * Handles the unhandled exception.
         */
        public async Task HandleException(IApplicationBuilder app, HttpContext context)
        {
            // Getting the path of the unhandled exception.
            var ex = context.Features.Get<IExceptionHandlerPathFeature>();

            // Ensuring we have access to the exception handler path feature before proceeding.
            if (ex != null)
            {
                // Defaulting status code and response Content-Type.
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                // Try custom handler first, and if no handler found, resorting to default handler.
                if (!await TryCustomExceptionHandler(app, ex, context))
                    await DefaultHandler(app, context, ex);
            }
        }

        #region [ -- Private helper methods -- ]

        /*
         * Tries to execute custom exception handler, and if we can find a custom handler,
         * returning true to caller - Otherwise returning false.
         */
        async Task<bool> TryCustomExceptionHandler(
            IApplicationBuilder app,
            IExceptionHandlerPathFeature ex,
            HttpContext context)
        {
            /*
             * Figuring out sections of path of invocation,
             * removing last part which is the filename we're executing, in addition
             * to the virtual "magic" parts of the URL.
             */
            var sections = ex.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var folders = sections.Skip(1).Take(sections.Length - 2);

            // Iterating upwards in hierarchy to see if we have a custom exception handler in folders upwards.
            while (true)
            {
                // Checking if we can find en "exceptions.hl" file in currently iterated folder.
                var filename = string.Join("/", folders) + "/" + "exceptions.hl";
                var path = Utilities.RootFolder + filename;
                if (File.Exists(path))
                {
                    // File exists, invoking it as a Hyperlambda file passing in exception arguments.
                    var args = new Node("", filename);
                    args.Add(new Node("message", ex.Error.Message));
                    args.Add(new Node("path", ex.Path));
                    args.Add(new Node("stack", ex.Error.StackTrace));
                    args.Add(new Node("source", ex.Error.Source));

                    /*
                     * Checking if this is a Hyperlambda exception, at which point we further
                     * parametrise invocation to exception file invocation with properties
                     * from Hyperlambda exception class.
                     */
                    if (ex.Error is HyperlambdaException hypEx)
                    {
                        if (!string.IsNullOrEmpty(hypEx.FieldName))
                            args.Add(new Node("field", hypEx.FieldName));
                        args.Add(new Node("status", hypEx.Status));
                        args.Add(new Node("public", hypEx.IsPublic));
                    }
                    var signaler = app.ApplicationServices.GetService<ISignaler>();
                    await signaler.SignalAsync("io.file.execute", args);

                    // Returning response according to result of above invocation.
                    var msg = args.Children.FirstOrDefault(x => x.Name == "message")?.Get<string>();
                    var response = new JObject
                    {
                        ["message"] = msg ?? GetDefaultExceptionMessage(),
                    };
                    var field = args.Children.FirstOrDefault(x => x.Name == "field")?.Get<string>();
                    if (!string.IsNullOrEmpty(field))
                        response["field"] = field;
                    context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    context.Response.StatusCode = args.Children.FirstOrDefault(x => x.Name == "status")?.Get<int>() ?? 500;
                    await context.Response.WriteAsync(response.ToString(Newtonsoft.Json.Formatting.Indented));

                    // Exception was handled.
                    return true;
                }

                // Checking if we have more folders to traverse upwards in hierarchy.
                if (!folders.Any())
                    return false;

                // Removing last folder and continuing iteration.
                folders = folders.Take(folders.Count() - 1);
            }
        }

        /*
         * Default handler that will kick in if no "exception.hl" file is found.
         *
         * This one simply logs the exception as is, and returns the exception response accordingly.
         */
        async Task DefaultHandler(
            IApplicationBuilder app,
            HttpContext context,
            IExceptionHandlerPathFeature ex)
        {
            var logger = app.ApplicationServices.GetService<ILogger>();
            try
            {
                await logger.ErrorAsync($"Unhandled exception occurred '{ex.Error.Message}' at '{ex.Path}'", ex.Error);
            }
            catch
            {
                // Silently catching to avoid new exception due to logger not being configured correctly ...
            }

            // Making sure we return exception according to specifications to caller as JSON of some sort.
            var response = GetExceptionResponse(ex, context, ex.Error.Message);
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            await context.Response.WriteAsync(response.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        /*
         * Helper method to create a JSON result from an exception, and returning
         * the result to the caller.
         */
        JObject GetExceptionResponse(
            IExceptionHandlerPathFeature ex,
            HttpContext context,
            string msg)
        {
            // Checking if exception is a HyperlambdaException, which is handled in a custom way.
            var hypEx = ex.Error as HyperlambdaException;
            if (hypEx != null)
            {
                /*
                 * Checking if caller wants to expose exception details to client,
                 * and retrieving status code, etc from exception details.
                 */
                context.Response.StatusCode = hypEx.Status;
                if (hypEx.IsPublic)
                {
                    // Exception details is supposed to be publicly visible.
                    var response = new JObject
                    {
                        ["message"] = msg,
                    };

                    /*
                     * Checking if we've got a field name of some sort, which allows client
                     * to semantically display errors related to validators, or fields of some sort,
                     * creating more detailed feedback to the user.
                     */
                    if (!string.IsNullOrEmpty(hypEx.FieldName))
                        response["field"] = hypEx.FieldName;
                    return response;
                }
                else
                {
                    // Exception details is not supposed to be publicly visible.
                    return new JObject
                    {
                        ["message"] = GetDefaultExceptionMessage()
                    };
                }
            }
            else
            {
                // Default exception response, returned if exception is not Hyperlambda exception.
                return new JObject
                {
                    ["message"] = GetDefaultExceptionMessage()
                };
            }
        }

        /*
         * Default exception response, returned unless exception explicitly
         * wants to override response content.
         */
        string GetDefaultExceptionMessage()
        {
            return "Guru meditation, come back when Universe is in order!";
        }

        #endregion
    }
}