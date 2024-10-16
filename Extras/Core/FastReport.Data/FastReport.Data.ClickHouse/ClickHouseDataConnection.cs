﻿using ClickHouse.Client.ADO;
using ClickHouse.Client.ADO.Adapters;
using ClickHouse.Client.ADO.Parameters;
using ClickHouse.Client.ADO.Readers;
using ClickHouse.Client.Types;
using ClickHouse.Client.Utility;
using FastReport.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastReport.ClickHouse
{
    public partial class ClickHouseDataConnection : DataConnectionBase
    {
        private void GetDBObjectNames(string name, List<string> list)
        {
            DataTable schema = null;
            DbConnection connection = GetConnection();
            try
            {
                OpenConnection(connection);
                if(name == "Tables")
                    schema = DescribeTables(connection, connection.Database);
                else
                    schema = connection.GetSchema(name, new string[] { connection.Database });
            }
            finally
            {
                DisposeConnection(connection);
            }
            foreach (DataRow row in schema.Rows)
            {
                list.Add(row["name"].ToString());
            }
        }

        private static DataTable DescribeTables(DbConnection connection, string database)
        {
            var command = connection.CreateCommand();
            var query = new StringBuilder("show tables in ");
            query.Append(database);
            command.CommandText = query.ToString();
            DataTable result = new DataTable();
            using (var adapter = new ClickHouseDataAdapter())
            {
                adapter.SelectCommand = command;
                adapter.Fill(result);
            }
            return result;
        }

        public override string QuoteIdentifier(string value, DbConnection connection)
        {
            return "\"" + value + "\"";
        }
        public override Type GetConnectionType()
        {
            return typeof(ClickHouseConnection);
        }
        public override string[] GetTableNames()
        {
            List<string> list = new List<string>();
            GetDBObjectNames("Tables", list);
            return list.ToArray();
        }

        public override DbDataAdapter GetAdapter(string selectCommand, DbConnection connection, CommandParameterCollection parameters)
        {
            ClickHouseDataAdapter clickHouseDataAdapter = new ClickHouseDataAdapter();
            var command = connection.CreateCommand() as ClickHouseCommand;

            foreach (CommandParameter p in parameters)
            {
                selectCommand = selectCommand.Replace($"@{p.Name}", $"{{{p.Name}:{(ClickHouseTypeCode)p.DataType}}}");
                command.AddParameter(p.Name, ((ClickHouseTypeCode)p.DataType).ToString(), p.Value);
            }
            command.CommandText = selectCommand;
            clickHouseDataAdapter.SelectCommand = command;
            return clickHouseDataAdapter;
        }

        private string PrepareSelectCommand(string selectCommand, string tableName, DbConnection connection)
        {
            if (String.IsNullOrEmpty(selectCommand))
            {
                selectCommand = "select * from " + QuoteIdentifier(tableName, connection);
            }
            return selectCommand;
        }

        private IEnumerable<DataColumn> GetColumns(ClickHouseDataReader reader)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnType = reader.GetFieldType(i);

                yield return new DataColumn(reader.GetName(i), columnType);
            }
        }

        public override void FillTableSchema(DataTable table, string selectCommand, CommandParameterCollection parameters)
        {
            ClickHouseConnection clickHouseConnection = GetConnection() as ClickHouseConnection;

            try
            {
                OpenConnection(clickHouseConnection);

                selectCommand = PrepareSelectCommand(selectCommand, table.TableName, clickHouseConnection);
                /*To reduce size of traffic and size of answer from ClickHouse server.
                  Because FillSchema doesn't work in this ADO.NET library.
                  LIMIT 0 gets an empty set, but we still have list of desired columns
                  Probably can be a better way.
                 */
                selectCommand += " LIMIT 0"; 
                ClickHouseCommand clickHouseCommand = clickHouseConnection.CreateCommand();

                foreach (CommandParameter p in parameters)
                {
                    selectCommand = selectCommand.Replace($"@{p.Name}", $"{{{p.Name}:{(ClickHouseTypeCode)p.DataType}}}");
                    if (p.Value is Variant value)
                    {
                        if (value.Type == typeof(string))
                            clickHouseCommand.AddParameter(p.Name, ((ClickHouseTypeCode)p.DataType).ToString(), VariantToClrType(value, (ClickHouseTypeCode)p.DataType)); 
                        else
                            clickHouseCommand.AddParameter(p.Name, ((ClickHouseTypeCode)p.DataType).ToString(), value.ToType(value.Type));
                    }
                    else
                        clickHouseCommand.AddParameter(p.Name, ((ClickHouseTypeCode)p.DataType).ToString(), p.Value);
                }
                clickHouseCommand.CommandText = selectCommand;
                using (ClickHouseDataReader reader = clickHouseCommand.ExecuteReader() as ClickHouseDataReader)
                {
                    var clms = GetColumns(reader);
                    table.Columns.AddRange(clms.ToArray());
                }
            }
            finally
            {
                DisposeConnection(clickHouseConnection);
            }
        }

        private object VariantToClrType(Variant value, ClickHouseTypeCode type)
        {
            if (value.ToString() == "" && type != ClickHouseTypeCode.Nothing)
                return null;

            switch (type)
            {
                case ClickHouseTypeCode.Enum8:
                case ClickHouseTypeCode.Int8:
                    {
                        sbyte val = 0;
                        sbyte.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.Enum16:
                case ClickHouseTypeCode.Int16:
                    {
                        short val = 0;
                        short.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.Int32:
                    {
                        int val = 0;
                        int.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.Int64:
                    {
                        long val = 0;
                        long.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.UInt8:
                    {
                        byte val = 0;
                        byte.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.UInt16:
                    {
                        ushort val = 0;
                        ushort.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.UInt32:
                    {
                        uint val = 0;
                        uint.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.UInt64:
                    {
                        ulong val = 0;
                        ulong.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.Date:
                    {
                        DateTime val = DateTime.Now;
                        DateTime.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.DateTime:
                case ClickHouseTypeCode.DateTime64:
                    {
                        DateTimeOffset val = DateTimeOffset.Now;
                        DateTimeOffset.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.Decimal:
                    {
                        decimal val = 0;
                        decimal.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.Float32:
                    {
                        float val = 0;
                        float.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.Float64:
                    {
                        double val = 0;
                        double.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.UUID:
                    {
                        Guid val = Guid.Empty;
                        Guid.TryParse(value.ToString(), out val);
                        return val;
                    }
                case ClickHouseTypeCode.IPv6:
                case ClickHouseTypeCode.IPv4:
                    {
                        try
                        {
                            return IPAddress.Parse(value.ToString());
                        }
                        catch
                        {
                            return IPAddress.None;
                        }
                    }
                case ClickHouseTypeCode.Nothing:
                    return DBNull.Value;
                case ClickHouseTypeCode.Array:
                case ClickHouseTypeCode.Nested:
                case ClickHouseTypeCode.Tuple:
                case ClickHouseTypeCode.Nullable:
                case ClickHouseTypeCode.LowCardinality:
                    throw new NotImplementedException();
    
                default:
                    return value.ToString();
            }
        }
    }
}
