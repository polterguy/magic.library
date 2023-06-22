/*
 * Magic Cloud, copyright Aista, Ltd. See the attached LICENSE file for details.
 */

using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using magic.lambda.sqlite;

namespace magic.library.internals
{
    /*
     * Internal helper class to help wire up plugins for SQLite.
     */
    internal class SQLiteInitializer : IInitializer
    {
        public void Initialize(SqliteConnection connection)
        {
            connection.EnableExtensions();
            connection.Open();

            // We don't have sqlite-vss on Windows :/
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using (var cmd = connection.CreateCommand())
                {
                    var cmdTxt = "";
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        cmdTxt = @"select load_extension(""./sqlite-plugins/deno-linux-x86_64.vector0"", ""sqlite3_vector_init"");select load_extension(""./sqlite-plugins/deno-linux-x86_64.vss0"", ""sqlite3_vss_init"");";
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm || RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                            cmdTxt = @"select load_extension(""./sqlite-plugins/deno-darwin-aarch64.vector0"", ""sqlite3_vector_init"");select load_extension(""./sqlite-plugins/deno-darwin-aarch64.vss0"", ""sqlite3_vss_init"");";
                        else
                            cmdTxt = @"select load_extension(""./sqlite-plugins/deno-darwin-x86_64.vector0"", ""sqlite3_vector_init"");select load_extension(""./sqlite-plugins/deno-darwin-x86_64.vss0"", ""sqlite3_vss_init"");";
                    }
                    cmd.CommandText = cmdTxt;
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}