/*
 * Magic, Copyright(c) Thomas Hansen 2019 - 2021, thomas@servergardens.com, all rights reserved.
 * See the enclosed LICENSE file for details.
 */

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;

namespace magic.library.internals
{
    /// <summary>
    /// Exception handler interface responsible for handling unhandled exceptions.
    /// </summary>
    public interface IExceptionHandler
    {
        /// <summary>
        /// Handles an exception in the specified HTTP context.
        /// </summary>
        /// <param name="context">HttpContext that triggered the exception</param>
        /// <param name="app">Needed to resolve services during exception handling</param>
        /// <returns>Awaitable task</returns>
        Task HandleException(IApplicationBuilder app, HttpContext context);
   }
}