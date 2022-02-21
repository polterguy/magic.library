/*
 * Magic Cloud, copyright Aista, Ltd. See the attached LICENSE file for details.
 */

using Microsoft.Extensions.Configuration;
using magic.data.common.contracts;

namespace magic.library.internals
{
    /*
     * Internal helper class to handle unhandled exceptions.
     */
    internal class AppSettingsDataSettings : IDataSettings
    {
        readonly IConfiguration _configuration;

        public AppSettingsDataSettings(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        #region [ -- Interface implementations -- ]

        public string DefaultDatabaseType { get => _configuration["magic:databases:default"]; }

        public string ConnectionString(string name, string databaseType = null)
        {
            return _configuration[$"magic:databases:{databaseType ?? DefaultDatabaseType}:{name}"];
        }

        #endregion
    }
}