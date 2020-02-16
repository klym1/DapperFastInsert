﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FastInsert.CsvHelper;

namespace FastInsert
{
    public static class FastInserter
    {
        public static Task FastInsertAsync<T>(this IDbConnection connection,
            IEnumerable<T> list,
            Action<FastInsertConfig>? conf = null)
        {
            return FastInsertAsync(connection, list.Cast<object>(), typeof(T), conf);
        }

        public static async Task FastInsertAsync(this IDbConnection connection,
            IEnumerable<object> list,
            Type entityType,
            Action<FastInsertConfig>? conf = null)
        {
            EnsureMySqlConnection(connection);

            var config = GetConfig(conf, entityType);

            if (!ConnectionStringValidator.ConnectionStringValid(connection.ConnectionString, out var error))
                throw new ArgumentException(error);

            var tableName = config.TableNameResolver.GetTableName();

            var writer = CsvWriterConfigurator.GetWriter(entityType);
            var tableDef = TableDefinitionFactory.BuildTableDefinition(entityType);

            foreach (var partition in EnumerableExtensions.GetPartitions(list, config.BatchSize))
            {
                var fileName = $"{Guid.NewGuid()}.csv";

                try
                {
                    var csvSettings = new CsvFileSettings
                    {
                        Delimiter = ";;",
                        LineEnding = Environment.NewLine,
                        Path = fileName,
                        FieldEscapedByChar = "\\\\",
                        FieldEnclosedByChar = "",
                    };

                    var query = BuildLoadDataQuery.BuildQuery(tableName, tableDef, csvSettings);

                    await writer.WriteAsync(partition, csvSettings);
                    await connection.ExecuteAsync(query);
                }
                finally
                {
                    config.Writer?.WriteLine(fileName + ":");
                    config.Writer?.WriteLine(File.ReadAllText(fileName));

                    File.Delete(fileName);
                }
            }
        }

        private static void EnsureMySqlConnection(IDbConnection connection)
        {
            var type = connection.GetType().ToString();

            if (!type.Contains("MySqlConnection"))
                throw new ArgumentException("This extension can only be used with MySqlConnection");
        }

        private static FastInsertConfig GetConfig(Action<FastInsertConfig> conf, Type entityType)
        {
            var config = new FastInsertConfig(entityType);
            conf?.Invoke(config);
            return config;
        }
    }
}
