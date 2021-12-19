/*
 * Magic Cloud, copyright Aista, Ltd. See the attached LICENSE file for details.
 */

using System.IO;
using Microsoft.Extensions.Configuration;
using magic.node.contracts;

namespace magic.library.internals
{
    internal sealed class RootResolver : IRootResolver
    {
        public RootResolver(IConfiguration configuration)
        {
            RootFolder = (configuration["magic:io:root-folder"] ?? "~/files/")
                .Replace("~", Directory.GetCurrentDirectory())
                .Replace("\\", "/")
                .TrimEnd('/') + "/";
        }

        public string RootFolder { get; }

        public string RelativePath(string path)
        {
            // Making sure we remove RootFolder, but keep the initial slash (/).
            return path.Substring(RootFolder.Length - 1);
        }

        public string AbsolutePath(string path)
        {
            // RootFolder should always start with a slash (/).
            return RootFolder + path.Replace("\\", "/").TrimStart('/');
        }
   }
}