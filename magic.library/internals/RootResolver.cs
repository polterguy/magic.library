/*
 * Magic Cloud, copyright Aista, Ltd. See the attached LICENSE file for details.
 */

using System.IO;
using Microsoft.Extensions.Configuration;
using magic.node.contracts;
using magic.node.extensions;

namespace magic.library.internals
{
    internal sealed class RootResolver : IRootResolver
    {
        public RootResolver(IConfiguration configuration)
        {
            DynamicFiles = (configuration["magic:io:root-folder"] ?? "~/files/")
                .Replace("~", Directory.GetCurrentDirectory())
                .Replace("\\", "/")
                .TrimEnd('/') + "/";
            RootFolder = Directory.GetCurrentDirectory()
                .Replace("\\", "/")
                .TrimEnd('/') + "/";
        }

        public string DynamicFiles { get; }

        public string RootFolder { get; }

        public string RelativePath(string path)
        {
            // Sanity checking invocation.
            if (!path.StartsWith(DynamicFiles))
                throw new HyperlambdaException("Tried to create a relative path out of a path that is not absolute");

            // Making sure we remove DynamicFiles, but keep the initial slash (/).
            return path.Substring(DynamicFiles.Length - 1);
        }

        public string AbsolutePath(string path)
        {
            // DynamicFiles should always start with a slash (/).
            return DynamicFiles + path.Replace("\\", "/").TrimStart('/');
        }
    }
}