using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace PluginMerge.Merge;

public class MergeHandler
{
    private readonly PluginMergeConfig _config;
    private readonly MergeConfig _merge;
    private readonly ILogger _logger;
    private List<FileHandler> _files = new();

    public MergeHandler(PluginMergeConfig config)
    {
        _config = config;
        _merge = config.Merge;
        _logger = LogBuilder.GetLogger<MergeHandler>();
    }

    public async Task Run()
    {
        Stopwatch sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting Plugin Merge Version: {Version} Mode: {Mode}", typeof(Program).Assembly.GetName().Version, _merge.CreatorMode);
        _logger.LogInformation("Input Paths: {Input}", string.Join(", ", _merge.InputPaths.Select(p => p.ToFullPath())));
        
        foreach (string path in _merge.OutputPaths)
        {
            if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            {
                _logger.LogDebug("Output path doesn't exist. Creating output path: {Path}", path);
                Directory.CreateDirectory(path);
            }
        }
        
        List<string> finalFiles = _merge.FinalFiles.ToList();

        FileScanner scanner = new(_merge.InputPaths, "*.cs", _merge.IgnorePaths, _merge.IgnoreFiles.Concat(finalFiles));
        foreach (ScannedFile file in scanner.ScanFiles())
        {
            _files.Add(new FileHandler(file, _config.RegionPathTrimLeft, _config.RegionPathTrimRight));
        }

        CSharpParseOptions options = new(_config.PlatformSettings.Lang);

        await Task.WhenAll(_files.Select(f => f.ProcessFile(_config.PlatformSettings, options))).ConfigureAwait(false);

        _files = _files.Where(f => !f.IsExcludedFile()).OrderBy(f => f.Order).ToList();

        if (string.IsNullOrWhiteSpace(_merge.PluginName))
        {
            FileHandler plugin = _files.FirstOrDefault(f => f.PluginData is { Title: not null, Description: not null });
            if (plugin?.PluginData is not null)
            {
                _merge.PluginName = plugin.PluginData.ClassName;
            }
        }

        finalFiles = _merge.FinalFiles.ToList();
        string[] finalPaths = finalFiles.Select(f => f.ToFullPath()).ToArray();
        _files.RemoveAll(f => finalPaths.Contains(f.FilePath.ToFullPath()));

        FileCreator creator = new(_config, _files);
        if (!creator.Create())
        {
            return;
        }

        string code = creator.ToCode();

        // SyntaxNode parsed = await CSharpSyntaxTree.ParseText(code, options).GetRootAsync().ConfigureAwait(false);
        // SourceText text = await parsed.NormalizeWhitespace("\n").SyntaxTree.GetTextAsync().ConfigureAwait(false);
        //
        // code = text.ToString();
        
        await Task.WhenAll(finalFiles.Select(f => File.WriteAllTextAsync(f, code))).ConfigureAwait(false);

        sw.Stop();
        _logger.LogInformation("Merged completed successfully in {Time}ms. {Mode} {Name} saved to: {File}", sw.ElapsedMilliseconds, _merge.CreatorMode, _merge.PluginName, string.Join(", ", finalFiles));
    }
}