using System.Data;
using Microsoft.Data.SqlClient;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Thin ADO.NET helpers for justified direct SQL (atomic counters, applocks).
/// Always use <see cref="SqlParameter"/> — never concatenate user input.
/// </summary>
public static class AdoSql
{
    public static SqlConnection CreateConnection(string connectionString) =>
        new(connectionString);

    public static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken ct = default)
    {
        var conn = CreateConnection(connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public static SqlParameter Param(string name, object? value, SqlDbType type)
    {
        var p = new SqlParameter(name, type)
        {
            Value = value ?? DBNull.Value
        };
        return p;
    }

    public static SqlParameter GuidParam(string name, Guid value) =>
        Param(name, value, SqlDbType.UniqueIdentifier);

    public static SqlParameter NVarChar(string name, string? value, int size) =>
        new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };

    public static SqlParameter IntParam(string name, int value) =>
        Param(name, value, SqlDbType.Int);

    public static async Task<int> ExecuteNonQueryAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        CancellationToken ct,
        params SqlParameter[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        if (parameters.Length > 0)
            cmd.Parameters.AddRange(parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<object?> ExecuteScalarAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        CancellationToken ct,
        params SqlParameter[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        if (parameters.Length > 0)
            cmd.Parameters.AddRange(parameters);
        return await cmd.ExecuteScalarAsync(ct);
    }

    public static async Task<SqlDataReader> ExecuteReaderAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        CancellationToken ct,
        params SqlParameter[] parameters)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        cmd.CommandType = CommandType.Text;
        if (parameters.Length > 0)
            cmd.Parameters.AddRange(parameters);
        return await cmd.ExecuteReaderAsync(CommandBehavior.Default, ct);
    }
}
