using Dapper;
using Npgsql;

namespace Hub.API.Tests.Integration;

[Collection("api")]
public sealed class SchemaTests
{
    private readonly string _connectionString;

    public SchemaTests(ApiTestFactory factory)
    {
        _ = factory;

        _connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection não foi definida pela ApiTestFactory.");
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed record ColumnInfo(
        string ColumnName,
        string DataType,
        string IsNullable,
        string? ColumnDefault,
        int? NumericPrecision,
        int? NumericScale);

    private async Task<IReadOnlyDictionary<string, ColumnInfo>> GetColumnsAsync(
        NpgsqlConnection connection, string tableName)
    {
        var rows = await connection.QueryAsync<ColumnInfo>(
            """
            SELECT column_name AS "ColumnName",
                   data_type AS "DataType",
                   is_nullable AS "IsNullable",
                   column_default AS "ColumnDefault",
                   numeric_precision AS "NumericPrecision",
                   numeric_scale AS "NumericScale"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @tableName
            """,
            new { tableName });

        return rows.ToDictionary(r => r.ColumnName);
    }

    private async Task<bool> TableExistsAsync(NpgsqlConnection connection, string tableName)
    {
        return await connection.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = @tableName
            )
            """,
            new { tableName });
    }

    private async Task<IReadOnlyList<string>> GetPrimaryKeyColumnsAsync(
        NpgsqlConnection connection, string tableName)
    {
        var rows = await connection.QueryAsync<string>(
            """
            SELECT a.attname
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indrelid
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey)
            WHERE c.relname = @tableName
              AND i.indisprimary
            ORDER BY array_position(i.indkey, a.attnum)
            """,
            new { tableName });

        return rows.ToList();
    }

    private sealed class ForeignKeyRow
    {
        public char ConfDelType { get; set; }
        public string[] ColunasOrigem { get; set; } = [];
        public string[] ColunasReferenciadas { get; set; } = [];
    }

    private sealed record ForeignKeyInfo(
        char ConfDelType,
        IReadOnlyList<string> ColunasOrigem,
        string TabelaReferenciada,
        IReadOnlyList<string> ColunasReferenciadas);

    private async Task<ForeignKeyInfo?> GetForeignKeyAsync(
        NpgsqlConnection connection, string tableName, string referencedTableName)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ForeignKeyRow>(
            """
            SELECT
                con.confdeltype AS "ConfDelType",
                (SELECT array_agg(a.attname ORDER BY k.ord)
                 FROM unnest(con.conkey) WITH ORDINALITY AS k(attnum, ord)
                 JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum) AS "ColunasOrigem",
                (SELECT array_agg(a.attname ORDER BY k.ord)
                 FROM unnest(con.confkey) WITH ORDINALITY AS k(attnum, ord)
                 JOIN pg_attribute a ON a.attrelid = con.confrelid AND a.attnum = k.attnum) AS "ColunasReferenciadas"
            FROM pg_constraint con
            JOIN pg_class src ON src.oid = con.conrelid
            JOIN pg_class dst ON dst.oid = con.confrelid
            WHERE con.contype = 'f'
              AND src.relname = @tableName
              AND dst.relname = @referencedTableName
            """,
            new { tableName, referencedTableName });

        return row is null
            ? null
            : new ForeignKeyInfo(row.ConfDelType, row.ColunasOrigem, referencedTableName, row.ColunasReferenciadas);
    }

    private void AssertColumn(
        IReadOnlyDictionary<string, ColumnInfo> columns,
        string tableName,
        string columnName,
        string expectedDataType,
        bool expectedNullable,
        bool expectNoDefault = true)
    {
        Assert.True(
            columns.ContainsKey(columnName),
            $"{tableName}: coluna '{columnName}' não existe. Colunas encontradas: [{string.Join(", ", columns.Keys)}]");

        var column = columns[columnName];

        Assert.True(
            string.Equals(column.DataType, expectedDataType, StringComparison.Ordinal),
            $"{tableName}.{columnName}: tipo esperado '{expectedDataType}', encontrado '{column.DataType}'.");

        var expectedIsNullableFlag = expectedNullable ? "YES" : "NO";
        Assert.True(
            string.Equals(column.IsNullable, expectedIsNullableFlag, StringComparison.Ordinal),
            $"{tableName}.{columnName}: nullability esperada '{expectedIsNullableFlag}', encontrada '{column.IsNullable}'.");

        if (expectNoDefault)
        {
            Assert.True(
                column.ColumnDefault is null,
                $"{tableName}.{columnName}: esperava column_default IS NULL (valor vindo do código, " +
                $"não do banco — divergência deliberada da ARQUITETURA.md §4.1), " +
                $"mas encontrou default '{column.ColumnDefault}'.");
        }
    }

    [Fact]
    public async Task Instrumentos_TabelaExiste()
    {
        using var connection = await OpenConnectionAsync();

        var exists = await TableExistsAsync(connection, "instrumentos");

        Assert.True(exists, "Tabela 'instrumentos' não existe no schema public após as migrations.");
    }

    [Fact]
    public async Task Instrumentos_ColunasBatemComArquitetura()
    {
        using var connection = await OpenConnectionAsync();
        var columns = await GetColumnsAsync(connection, "instrumentos");

        var expected = new[]
        {
            "id", "classe", "nome_exibicao", "ativo_desde", "ativo_ate",
            "paga_cupom", "metadados", "criado_em",
        };

        Assert.True(
            expected.ToHashSet().SetEquals(columns.Keys),
            "instrumentos: conjunto de colunas divergente. Esperado: " +
            $"[{string.Join(", ", expected)}], encontrado: [{string.Join(", ", columns.Keys)}].");

        AssertColumn(columns, "instrumentos", "id", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "instrumentos", "classe", "text", expectedNullable: false);
        AssertColumn(columns, "instrumentos", "nome_exibicao", "text", expectedNullable: false);
        AssertColumn(columns, "instrumentos", "ativo_desde", "date", expectedNullable: true);
        AssertColumn(columns, "instrumentos", "ativo_ate", "date", expectedNullable: true);
        AssertColumn(columns, "instrumentos", "paga_cupom", "boolean", expectedNullable: false);
        AssertColumn(columns, "instrumentos", "metadados", "jsonb", expectedNullable: false);
        AssertColumn(columns, "instrumentos", "criado_em", "timestamp with time zone", expectedNullable: false);
    }

    [Fact]
    public async Task Instrumentos_ChavePrimaria_EhId()
    {
        using var connection = await OpenConnectionAsync();

        var pk = await GetPrimaryKeyColumnsAsync(connection, "instrumentos");

        Assert.Equal(new[] { "id" }, pk);
    }

    [Fact]
    public async Task Instrumentos_TemIndiceDeClasse()
    {
        using var connection = await OpenConnectionAsync();

        var indexDef = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'instrumentos'
              AND indexname = 'ix_instrumentos_classe'
            """);

        Assert.True(
            indexDef is not null,
            "Índice 'ix_instrumentos_classe' não existe em instrumentos (divergência deliberada nº4 " +
            "da ARQUITETURA.md §4.1 — a doc não o nomeia, mas a migration deve criá-lo).");

        Assert.True(
            indexDef!.Contains("(classe)", StringComparison.OrdinalIgnoreCase),
            $"ix_instrumentos_classe: esperava indexar a coluna 'classe', definição encontrada: '{indexDef}'.");
    }

    [Fact]
    public async Task Instrumentos_TemCheckConstraintDeClassePrefixoDoId()
    {
        using var connection = await OpenConnectionAsync();

        var contype = await connection.ExecuteScalarAsync<char?>(
            """
            SELECT con.contype
            FROM pg_constraint con
            JOIN pg_class c ON c.oid = con.conrelid
            WHERE c.relname = 'instrumentos'
              AND con.conname = 'ck_instrumentos_classe_prefixo'
            """);

        Assert.True(
            contype is not null,
            "instrumentos: não encontrei a constraint 'ck_instrumentos_classe_prefixo'.");

        Assert.True(
            contype == 'c',
            $"ck_instrumentos_classe_prefixo: esperava contype = 'c' (CHECK), encontrado '{contype}'.");
    }

    [Fact]
    public async Task Instrumentos_CheckConstraintDeClassePrefixoDoId_BarraClasseDivergenteDoPrefixo()
    {
        using var connection = await OpenConnectionAsync();

        var exception = await Record.ExceptionAsync(() => connection.ExecuteAsync(
            """
            INSERT INTO instrumentos (id, classe, nome_exibicao, paga_cupom, metadados, criado_em)
            VALUES ('td:instrumento-schema-tests-check', 'acao', 'Divergente', false, '{}', now())
            """));

        Assert.NotNull(exception);
        Assert.IsType<PostgresException>(exception);
        Assert.Equal(
            "ck_instrumentos_classe_prefixo",
            ((PostgresException)exception!).ConstraintName);
    }

    [Fact]
    public async Task InstrumentoFontes_TabelaExiste()
    {
        using var connection = await OpenConnectionAsync();

        var exists = await TableExistsAsync(connection, "instrumento_fontes");

        Assert.True(exists, "Tabela 'instrumento_fontes' não existe no schema public após as migrations.");
    }

    [Fact]
    public async Task InstrumentoFontes_ColunasBatemComArquitetura()
    {
        using var connection = await OpenConnectionAsync();
        var columns = await GetColumnsAsync(connection, "instrumento_fontes");

        var expected = new[] { "instrumento_id", "fonte", "codigo_na_fonte" };

        Assert.True(
            expected.ToHashSet().SetEquals(columns.Keys),
            "instrumento_fontes: conjunto de colunas divergente. Esperado: " +
            $"[{string.Join(", ", expected)}], encontrado: [{string.Join(", ", columns.Keys)}].");

        AssertColumn(columns, "instrumento_fontes", "instrumento_id", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "instrumento_fontes", "fonte", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "instrumento_fontes", "codigo_na_fonte", "text", expectedNullable: false, expectNoDefault: false);
    }

    [Fact]
    public async Task InstrumentoFontes_ChavePrimaria_EhInstrumentoIdFonteNaOrdem()
    {
        using var connection = await OpenConnectionAsync();

        var pk = await GetPrimaryKeyColumnsAsync(connection, "instrumento_fontes");

        Assert.Equal(new[] { "instrumento_id", "fonte" }, pk);
    }

    [Fact]
    public async Task InstrumentoFontes_TemUniqueDeFonteECodigoNaFonte()
    {
        using var connection = await OpenConnectionAsync();

        var uniqueColumnSets = await connection.QueryAsync<string>(
            """
            SELECT string_agg(a.attname, ',' ORDER BY array_position(i.indkey, a.attnum))
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indrelid
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey)
            WHERE c.relname = 'instrumento_fontes'
              AND i.indisunique
              AND NOT i.indisprimary
            GROUP BY i.indexrelid
            """);

        var sets = uniqueColumnSets.ToList();

        Assert.True(
            sets.Contains("fonte,codigo_na_fonte"),
            "instrumento_fontes: esperava UNIQUE (fonte, codigo_na_fonte), " +
            $"constraints unique encontradas: [{string.Join(" | ", sets)}].");
    }

    [Fact]
    public async Task InstrumentoFontes_FkInstrumentoId_ApontaParaInstrumentosComRestrict()
    {
        using var connection = await OpenConnectionAsync();

        var fk = await GetForeignKeyAsync(connection, "instrumento_fontes", "instrumentos");

        Assert.True(
            fk is not null,
            "instrumento_fontes: não encontrei FK para instrumentos(id).");

        Assert.Equal(new[] { "instrumento_id" }, fk!.ColunasOrigem);
        Assert.Equal("instrumentos", fk.TabelaReferenciada);
        Assert.Equal(new[] { "id" }, fk.ColunasReferenciadas);

        Assert.True(
            fk.ConfDelType == 'r',
            $"instrumento_fontes.instrumento_id -> instrumentos(id): esperava ON DELETE RESTRICT " +
            $"(confdeltype = 'r'), encontrado '{fk.ConfDelType}'.");
    }

    [Fact]
    public async Task Precos_TabelaExiste()
    {
        using var connection = await OpenConnectionAsync();

        var exists = await TableExistsAsync(connection, "precos");

        Assert.True(exists, "Tabela 'precos' não existe no schema public após as migrations.");
    }

    [Fact]
    public async Task Precos_ColunasBatemComArquitetura()
    {
        using var connection = await OpenConnectionAsync();
        var columns = await GetColumnsAsync(connection, "precos");

        var expected = new[]
        {
            "instrumento_id", "data_ref", "campo", "fonte", "valor",
            "revisao", "observado_em",
        };

        Assert.True(
            expected.ToHashSet().SetEquals(columns.Keys),
            "precos: conjunto de colunas divergente. Esperado: " +
            $"[{string.Join(", ", expected)}], encontrado: [{string.Join(", ", columns.Keys)}].");

        AssertColumn(columns, "precos", "instrumento_id", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "precos", "data_ref", "date", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "precos", "campo", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "precos", "fonte", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "precos", "valor", "numeric", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "precos", "revisao", "integer", expectedNullable: false);
        AssertColumn(columns, "precos", "observado_em", "timestamp with time zone", expectedNullable: false);

        var valorColumn = columns["valor"];
        Assert.True(
            valorColumn.NumericPrecision == 18 && valorColumn.NumericScale == 6,
            "precos.valor: esperava numeric(18,6), encontrado " +
            $"numeric({valorColumn.NumericPrecision},{valorColumn.NumericScale}).");
    }

    [Fact]
    public async Task Precos_ChavePrimaria_NaOrdemDaArquitetura()
    {
        using var connection = await OpenConnectionAsync();

        var pk = await GetPrimaryKeyColumnsAsync(connection, "precos");

        Assert.Equal(
            new[] { "instrumento_id", "data_ref", "campo", "fonte", "revisao" },
            pk);
    }

    [Fact]
    public async Task Precos_FkInstrumentoId_ApontaParaInstrumentosComRestrict()
    {
        using var connection = await OpenConnectionAsync();

        var fk = await GetForeignKeyAsync(connection, "precos", "instrumentos");

        Assert.True(
            fk is not null,
            "precos: não encontrei FK para instrumentos(id).");

        Assert.Equal(new[] { "instrumento_id" }, fk!.ColunasOrigem);
        Assert.Equal("instrumentos", fk.TabelaReferenciada);
        Assert.Equal(new[] { "id" }, fk.ColunasReferenciadas);

        Assert.True(
            fk.ConfDelType == 'r',
            $"precos.instrumento_id -> instrumentos(id): esperava ON DELETE RESTRICT " +
            $"(confdeltype = 'r'), encontrado '{fk.ConfDelType}'.");
    }

    [Fact]
    public async Task Precos_IndiceDeLookup_EhPorInstrumentoEDataRefDescendente()
    {
        using var connection = await OpenConnectionAsync();

        var indexDef = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'precos'
              AND indexname = 'ix_precos_lookup'
            """);

        Assert.True(indexDef is not null, "Índice 'ix_precos_lookup' não existe em precos.");

        Assert.True(
            indexDef!.Contains("data_ref DESC", StringComparison.OrdinalIgnoreCase),
            "ix_precos_lookup: esperava 'data_ref DESC' na definição do índice " +
            $"(ARQUITETURA.md §4.1), encontrado: '{indexDef}'.");

        Assert.True(
            indexDef.Contains("instrumento_id", StringComparison.OrdinalIgnoreCase),
            $"ix_precos_lookup: esperava indexar 'instrumento_id' também, encontrado: '{indexDef}'.");
    }

    [Fact]
    public async Task Outbox_TabelaExiste()
    {
        using var connection = await OpenConnectionAsync();

        var exists = await TableExistsAsync(connection, "outbox");

        Assert.True(exists, "Tabela 'outbox' não existe no schema public após as migrations.");
    }

    [Fact]
    public async Task Outbox_ColunasBatemComArquitetura()
    {
        using var connection = await OpenConnectionAsync();
        var columns = await GetColumnsAsync(connection, "outbox");

        var expected = new[]
        {
            "id", "tipo", "routing_key", "payload", "criado_em", "publicado_em",
        };

        Assert.True(
            expected.ToHashSet().SetEquals(columns.Keys),
            "outbox: conjunto de colunas divergente. Esperado: " +
            $"[{string.Join(", ", expected)}], encontrado: [{string.Join(", ", columns.Keys)}].");

        AssertColumn(columns, "outbox", "id", "bigint", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "outbox", "tipo", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "outbox", "routing_key", "text", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "outbox", "payload", "jsonb", expectedNullable: false, expectNoDefault: false);
        AssertColumn(columns, "outbox", "criado_em", "timestamp with time zone", expectedNullable: false);
        AssertColumn(columns, "outbox", "publicado_em", "timestamp with time zone", expectedNullable: true);
    }

    [Fact]
    public async Task Outbox_Id_EhIdentityColumn_NaoBigserial()
    {
        using var connection = await OpenConnectionAsync();

        var isIdentity = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT is_identity FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'outbox' AND column_name = 'id'
            """);

        Assert.True(
            isIdentity is not null,
            "outbox.id: coluna não encontrada.");

        Assert.True(
            string.Equals(isIdentity, "YES", StringComparison.Ordinal),
            "outbox.id: esperava is_identity = 'YES' (GENERATED BY DEFAULT AS IDENTITY, " +
            $"divergência deliberada nº2 da ARQUITETURA.md §4.1, que mostra bigserial), encontrado '{isIdentity}'.");

        var identityGeneration = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT identity_generation FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'outbox' AND column_name = 'id'
            """);

        Assert.True(
            string.Equals(identityGeneration, "BY DEFAULT", StringComparison.Ordinal),
            $"outbox.id: esperava identity_generation = 'BY DEFAULT', encontrado '{identityGeneration}'.");
    }

    [Fact]
    public async Task Outbox_ChavePrimaria_EhId()
    {
        using var connection = await OpenConnectionAsync();

        var pk = await GetPrimaryKeyColumnsAsync(connection, "outbox");

        Assert.Equal(new[] { "id" }, pk);
    }

    [Fact]
    public async Task Outbox_IndicePendentes_EhParcialSobrePublicadoEmNulo()
    {
        using var connection = await OpenConnectionAsync();

        var indexDef = await connection.ExecuteScalarAsync<string?>(
            """
            SELECT indexdef FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'outbox'
              AND indexname = 'ix_outbox_pendentes'
            """);

        Assert.True(indexDef is not null, "Índice 'ix_outbox_pendentes' não existe em outbox.");

        Assert.True(
            indexDef!.Contains("WHERE", StringComparison.OrdinalIgnoreCase)
            && indexDef.Contains("publicado_em", StringComparison.OrdinalIgnoreCase)
            && indexDef.Contains("IS NULL", StringComparison.OrdinalIgnoreCase),
            "ix_outbox_pendentes: esperava índice PARCIAL com predicado " +
            $"'publicado_em IS NULL' (ARQUITETURA.md §4.1), encontrado: '{indexDef}'.");
    }
}
