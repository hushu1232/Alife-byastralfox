using Microsoft.AspNetCore.Components;

namespace Alife.Framework;

/// <summary>
/// 插件 UI 基类。
/// 所有的插件自定义配置/管理界面应继承此类。
/// </summary>
public abstract class PluginUIBase : ComponentBase
{
    /// <summary>
    /// 当前插件的类型。
    /// </summary>
    [Parameter] public Type PluginType { get; set; } = null!;

    /// <summary>
    /// 当前关联的角色（如果有）。
    /// 在全局配置中心此项为空。
    /// </summary>
    [Parameter] public Character? Character { get; set; }

    /// <summary>
    /// 当前关联的运行活动（如果有）。
    /// 用于显示实时运行状态。
    /// </summary>
    [Parameter] public ChatActivity? ChatActivity { get; set; }
}
