using System.Linq;
using System.Reflection;

// Every message shown before the game font is available belongs here.
// The editor migration generates and verifies the local font from this corpus.
public static class StartupUiText
{
    public const string Ready = "资源已就绪";
    public const string CheckingResources = "检查游戏资源";
    public const string InitializationFailed = "资源目录初始化失败";
    public const string CheckingCatalog = "检查资源目录";
    public const string CatalogCheckFailed = "检查远程目录失败";
    public const string UpdatingCatalog = "更新资源目录";
    public const string CatalogUpdateFailed = "更新资源目录失败";
    public const string CatalogChanged = "资源目录已更新";
    public const string CheckingCache = "检查资源";
    public const string CacheCheckFailed = "资源检查失败";
    public const string CalculatingFont = "计算游戏字体下载量";
    public const string DownloadingFont = "下载游戏字体 ";
    public const string FontDownloadFailed = "游戏字体下载失败";
    public const string FontLoadFailed = "游戏字体加载失败";
    public const string RequiredContentMissing = "必需的远程资源未登记到目录";
    public const string RemoteAssetMissing = "必需的远程资源不存在: ";
    public const string CalculatingTable = "计算物品目录下载量";
    public const string ItemCatalogMissing = "物品目录不存在";
    public const string DownloadingTable = "下载物品目录 ";
    public const string ItemCatalogDownloadFailed = "物品目录下载失败";
    public const string ItemTableLoadFailed = "物品总表加载失败";
    public const string RequiredIconsMissing = "必需的远程图标地址未配置";
    public const string CalculatingResources = "计算资源下载量";
    public const string ResourcesSizeFailed = "计算资源下载量失败";
    public const string DownloadingResources = "下载资源 ";
    public const string ResourcesDownloadFailed = "资源下载失败";
    public const string LoadingIcons = "加载图标";
    public const string RequiredIconLoadFailed = "必需的远程图标加载失败";
    public const string RetrySuffix = "，请重试";
    public const string Retry = "重试";

    public static string FontCharacters => new string(
        typeof(StartupUiText).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(info => info.IsLiteral && info.FieldType == typeof(string))
            .SelectMany(info => (string)info.GetRawConstantValue())
            .Concat(Enumerable.Range(32, 95).Select(value => (char)value))
            .Concat("□…　")
            .Distinct().OrderBy(character => character).ToArray());
}
