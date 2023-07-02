/*
 * Magic Cloud, copyright Aista, Ltd and Thomas Hansen. See the attached LICENSE file for details. For license inquiries you can send an email to thomas@ainiro.io
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
                    var cmdTxt = @"select load_extension(""./sqlite-plugins/vector0"", ""sqlite3_vector_init"");select load_extension(""./sqlite-plugins/vss0"", ""sqlite3_vss_init"");";
                    cmd.CommandText = cmdTxt;
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}