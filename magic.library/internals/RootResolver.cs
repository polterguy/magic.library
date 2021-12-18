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
                .TrimEnd('/') + "/";
        }

        /// </inheritdocs>
        public string RootFolder { get; }

        /// </inheritdocs>
        public string RelativePath(string path)
        {
            return path.Substring(RootFolder.Length - 1);
        }

        /// </inheritdocs>
        public string AbsolutePath(string path)
        {
            return RootFolder + path.TrimStart(new char[] { '/', '\\' });
        }
   }
}