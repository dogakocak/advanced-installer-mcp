namespace AdvancedInstallerMcp.Msi;

// Thin wrapper over an MSI database handle. The server only reads packages; Create, Execute and Commit exist so
// tests can build small packages without Advanced Installer.
public sealed class MsiDatabase : IDisposable
{
    private readonly MsiHandle _handle;

    private MsiDatabase(MsiHandle handle) => _handle = handle;

    public static MsiDatabase OpenReadOnly(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("MSI package not found.", path);
        }

        return Open(path, NativeMethods.OpenReadOnly);
    }

    internal static MsiDatabase Create(string path) => Open(path, NativeMethods.OpenCreate);

    // Values for the `?` markers in the query are bound in order: string, int or null (and a FileInfo for a stream
    // column, which only the test helpers use).
    public IReadOnlyList<T> Query<T>(string sql, Func<MsiRecord, T> map, params object?[] parameters)
    {
        using var view = OpenAndExecute(sql, parameters);
        var rows = new List<T>();

        while (true)
        {
            var result = NativeMethods.MsiViewFetch(view, out var record);
            if (result == NativeMethods.ErrorNoMoreItems)
            {
                break;
            }

            MsiException.ThrowIfFailed(result, $"Fetching rows of \"{sql}\"");
            using var handle = MsiHandle.Own(record);
            rows.Add(map(new MsiRecord(handle)));
        }

        NativeMethods.MsiViewClose(view);
        return rows;
    }

    internal void Execute(string sql, params object?[] parameters)
    {
        using var view = OpenAndExecute(sql, parameters);
        NativeMethods.MsiViewClose(view);
    }

    internal void Commit() => MsiException.ThrowIfFailed(NativeMethods.MsiDatabaseCommit(_handle), "Commit");

    // Writes the summary properties Windows Installer checks before it opens a package: template (platform;language),
    // package code, schema 200, compressed source. Before Commit.
    internal void SetPackageSummary(string packageCode)
    {
        const uint template = 7, revision = 9, pageCount = 14, wordCount = 15;
        const uint vtI4 = 3, vtLpstr = 30;

        MsiException.ThrowIfFailed(NativeMethods.MsiGetSummaryInformation(_handle, null, 4, out var summaryHandle), "Summary information");
        using var summary = MsiHandle.Own(summaryHandle);
        MsiException.ThrowIfFailed(NativeMethods.MsiSummaryInfoSetProperty(summary, template, vtLpstr, 0, 0, "x64;1033"), "Template");
        MsiException.ThrowIfFailed(NativeMethods.MsiSummaryInfoSetProperty(summary, revision, vtLpstr, 0, 0, packageCode), "Package code");
        MsiException.ThrowIfFailed(NativeMethods.MsiSummaryInfoSetProperty(summary, pageCount, vtI4, 200, 0, null), "Schema");
        MsiException.ThrowIfFailed(NativeMethods.MsiSummaryInfoSetProperty(summary, wordCount, vtI4, 2, 0, null), "Source type");
        MsiException.ThrowIfFailed(NativeMethods.MsiSummaryInfoPersist(summary), "Summary persist");
    }

    public void Dispose() => _handle.Dispose();

    private static MsiDatabase Open(string path, nint mode)
    {
        MsiException.ThrowIfFailed(NativeMethods.MsiOpenDatabase(path, mode, out var handle), $"Opening \"{path}\"");
        return new MsiDatabase(MsiHandle.Own(handle));
    }

    private MsiHandle OpenAndExecute(string sql, object?[] parameters)
    {
        MsiException.ThrowIfFailed(NativeMethods.MsiDatabaseOpenView(_handle, sql, out var viewHandle), $"Query \"{sql}\"");
        var view = MsiHandle.Own(viewHandle);

        try
        {
            using var record = CreateParameterRecord(parameters);
            MsiException.ThrowIfFailed(NativeMethods.MsiViewExecute(view, record ?? MsiHandle.None), $"Executing \"{sql}\"");
            return view;
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    private static MsiHandle? CreateParameterRecord(object?[] parameters)
    {
        if (parameters.Length == 0)
        {
            return null;
        }

        var record = MsiHandle.Own(NativeMethods.MsiCreateRecord((uint)parameters.Length));

        try
        {
            BindParameters(record, parameters);
            return record;
        }
        catch
        {
            record.Dispose();
            throw;
        }
    }

    private static void BindParameters(MsiHandle record, object?[] parameters)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            var field = (uint)(i + 1);
            var result = parameters[i] switch
            {
                null => NativeMethods.ErrorSuccess,
                string text => NativeMethods.MsiRecordSetString(record, field, text),
                int number => NativeMethods.MsiRecordSetInteger(record, field, number),
                FileInfo file => NativeMethods.MsiRecordSetStream(record, field, file.FullName),
                var other => throw new ArgumentException($"Unsupported parameter type {other.GetType().Name}.", nameof(parameters)),
            };

            MsiException.ThrowIfFailed(result, $"Binding parameter {field}");
        }
    }
}
