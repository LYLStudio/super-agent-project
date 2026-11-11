using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core;

/// <summary>
/// IToolPlugin 介面 - 工具插件介面
/// 支援動態載入外部工具模組
/// </summary>
public interface IToolPlugin : ITool
{
    /// <summary>
    /// 插件名稱
    /// </summary>
    new string Name { get; }

    /// <summary>
    /// 插件描述
    /// </summary>
    new string Description { get; }

    /// <summary>
    /// 插件版本
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 插件作者
    /// </summary>
    string Author { get; }

    /// <summary>
    /// 初始化插件
    /// </summary>
    /// <param name="context">插件上下文</param>
    /// <returns>初始化結果</returns>
    Task<bool> InitializeAsync(PluginContext context, CancellationToken cancellationToken);

    /// <summary>
    /// 卸載插件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>卸載結果</returns>
    Task<bool> UnloadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// PluginContext - 插件上下文
/// 提供插件運行所需的環境與服務
/// </summary>
public class PluginContext
{
    /// <summary>
    /// 工具註冊表
    /// </summary>
    public IToolRegistry ToolRegistry { get; }

    /// <summary>
    /// 代理客戶端
    /// </summary>
    public IAgentClient AgentClient { get; }

    /// <summary>
    /// 插件目錄路徑
    /// </summary>
    public string PluginDirectory { get; }

    /// <summary>
    /// 應用程式基礎路徑
    /// </summary>
    public string BasePath { get; }

    /// <summary>
    /// 建立插件上下文
    /// </summary>
    /// <param name="toolRegistry">工具註冊表</param>
    /// <param name="agentClient">代理客戶端</param>
    /// <param name="pluginDirectory">插件目錄</param>
    /// <param name="basePath">應用程式基礎路徑</param>
    public PluginContext(IToolRegistry toolRegistry, IAgentClient agentClient, string pluginDirectory, string basePath)
    {
        ToolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        AgentClient = agentClient ?? throw new ArgumentNullException(nameof(agentClient));
        PluginDirectory = pluginDirectory ?? throw new ArgumentNullException(nameof(pluginDirectory));
        BasePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    }
}

/// <summary>
/// PluginLoader - 插件載入器
/// 支援從目錄動態載入插件模組
/// </summary>
public class PluginLoader
{
    private readonly string _pluginDirectory;
    private readonly List<IToolPlugin> _loadedPlugins = new();
    private readonly object _lock = new();

    /// <summary>
    /// 建立插件載入器
    /// </summary>
    /// <param name="pluginDirectory">插件目錄路徑</param>
    public PluginLoader(string pluginDirectory)
    {
        _pluginDirectory = pluginDirectory ?? throw new ArgumentNullException(nameof(pluginDirectory));
        if (!Directory.Exists(_pluginDirectory))
        {
            Directory.CreateDirectory(_pluginDirectory);
        }
    }

    /// <summary>
    /// 載入所有插件
    /// </summary>
    /// <param name="context">插件上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>載入的插件列表</returns>
    public async Task<List<IToolPlugin>> LoadAllPluginsAsync(PluginContext context, CancellationToken cancellationToken)
    {
        var loadedPlugins = new List<IToolPlugin>();

        lock (_lock)
        {
            // 清除現有插件
            foreach (var plugin in _loadedPlugins)
            {
                try
                {
                    _ = plugin.UnloadAsync(cancellationToken).GetAwaiter().GetResult();
                }
                catch
                {
                    // 插件卸載失敗不阻止其他插件載入
                }
            }
            _loadedPlugins.Clear();
        }

        // 掃描插件目錄中的 .dll 檔案
        if (Directory.Exists(_pluginDirectory))
        {
            var dllFiles = Directory.GetFiles(_pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly);

            foreach (var dllPath in dllFiles)
            {
                try
                {
                    var plugin = await LoadPluginFromAssemblyAsync(dllPath, context, cancellationToken);
                    if (plugin != null)
                    {
                        loadedPlugins.Add(plugin);
                    }
                }
                catch (Exception ex)
                {
                    // 插件載入失敗記錄但不阻止其他插件
                    Console.WriteLine($"插件載入失敗: {dllPath} - {ex.Message}");
                }
            }
        }

        return loadedPlugins;
    }

    /// <summary>
    /// 從組件載入插件
    /// </summary>
    /// <param name="assemblyPath">組件路徑</param>
    /// <param name="context">插件上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>插件實例或 null</returns>
    private async Task<IToolPlugin?> LoadPluginFromAssemblyAsync(string assemblyPath, PluginContext context, CancellationToken cancellationToken)
    {
        // 載入組件
        var assembly = System.Reflection.Assembly.LoadFrom(assemblyPath);

        // 查找實作 IToolPlugin 的類別
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsInterface || type.IsAbstract)
            {
                continue;
            }

            if (typeof(IToolPlugin).IsAssignableFrom(type))
            {
                try
                {
                    var plugin = (IToolPlugin?)Activator.CreateInstance(type);
                    if (plugin is not null)
                    {
                        // 初始化插件
                        var initialized = await plugin.InitializeAsync(context, cancellationToken);
                        if (initialized)
                        {
                            lock (_lock)
                            {
                                _loadedPlugins.Add(plugin);
                            }
                            return plugin;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"插件初始化失敗: {type.FullName} - {ex.Message}");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 取得所有載入的插件
    /// </summary>
    public List<IToolPlugin> GetLoadedPlugins()
    {
        lock (_lock)
        {
            return new List<IToolPlugin>(_loadedPlugins);
        }
    }

    /// <summary>
    /// 卸載所有插件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>卸載結果</returns>
    public async Task<bool> UnloadAllPluginsAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            foreach (var plugin in _loadedPlugins)
            {
                try
                {
                    _ = plugin.UnloadAsync(cancellationToken).GetAwaiter().GetResult();
                }
                catch
                {
                    // 插件卸載失敗不阻止其他插件卸載
                }
            }
            _loadedPlugins.Clear();
        }

        return true;
    }
}
